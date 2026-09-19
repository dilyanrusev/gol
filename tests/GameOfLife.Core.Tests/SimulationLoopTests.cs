using System.Diagnostics;
using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Core.Tests;

public class SimulationLoopTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private readonly Channel<UniverseSnapshot> _published = Channel.CreateUnbounded<UniverseSnapshot>();
    private SimulationLoop _loop = null!;
    private Task _run = null!;

    public Task InitializeAsync()
    {
        _loop = new SimulationLoop((s, ct) => _published.Writer.WriteAsync(s, ct).AsTask());
        _run = _loop.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _run;
    }

    private async Task<UniverseSnapshot> NextSnapshotAsync() => await _published.Reader.ReadAsync(_cts.Token);

    [Fact]
    public void Starts_empty_and_paused()
    {
        var s = _loop.Current;
        Assert.Equal(0, s.Population);
        Assert.Equal(0UL, s.Generation);
        Assert.False(s.Running);
        Assert.Equal(Cell.Centre, s.SeedCentre);
        Assert.Equal(SimulationLoop.DefaultGenerationsPerSecond, s.GenerationsPerSecond);
    }

    [Fact]
    public async Task Load_centres_pattern_publishes_snapshot_and_does_not_start()
    {
        var gun = KnownPatterns.GosperGliderGun();
        await _loop.LoadAsync(gun);

        var s = await NextSnapshotAsync();
        Assert.Equal(36, s.Population);
        Assert.Equal(0UL, s.Generation);
        Assert.False(s.Running);
        Assert.Equal(Cell.Centre.Offset(-18, -4).Offset(18, 4), s.SeedCentre);

        var expected = gun.ToUniverseCells(gun.TopLeftWhenCentredAt(Cell.Centre)).ToHashSet();
        Assert.Equal(expected, s.Cells.ToHashSet());
        Assert.Same(s, _loop.Current);
    }

    [Fact]
    public async Task Load_honours_an_explicit_origin()
    {
        var p = RleParser.Parse("#C origin 100 200\nx = 3, y = 1\n3o!");
        await _loop.LoadAsync(p);

        var s = await NextSnapshotAsync();
        Assert.Equal(new HashSet<Cell> { new(100, 200), new(101, 200), new(102, 200) }, s.Cells.ToHashSet());
        Assert.Equal(new Cell(101, 200), s.SeedCentre);
    }

    [Fact]
    public async Task Step_advances_one_generation_while_paused()
    {
        await _loop.LoadAsync(RleParser.Parse(KnownPatterns.Blinker));
        await NextSnapshotAsync();

        await _loop.StepAsync();
        var s = await NextSnapshotAsync();

        Assert.Equal(1UL, s.Generation);
        Assert.False(s.Running);
        Assert.Equal(3, s.Population);
    }

    [Fact]
    public async Task Start_runs_generations_at_the_configured_speed_and_pause_stops_them()
    {
        await _loop.LoadAsync(RleParser.Parse(KnownPatterns.Glider));
        await _loop.SetSpeedAsync(SimulationLoop.MaxGenerationsPerSecond);
        await _loop.StartAsync();

        UniverseSnapshot s;
        do s = await NextSnapshotAsync(); while (s.Generation < 5);
        Assert.True(s.Running);

        await _loop.PauseAsync();
        // Drain whatever was in flight, then confirm nothing else arrives.
        do s = await NextSnapshotAsync(); while (s.Running);
        var afterPause = _loop.Current.Generation;
        await Task.Delay(200);
        Assert.Equal(afterPause, _loop.Current.Generation);
        Assert.False(_loop.Current.Running);
    }

    [Fact]
    public async Task Reset_restores_the_seed_and_pauses()
    {
        await _loop.LoadAsync(RleParser.Parse(KnownPatterns.Glider));
        var seed = (await NextSnapshotAsync()).Cells.ToHashSet();

        await _loop.StepAsync();
        await _loop.StepAsync();
        await _loop.StartAsync();
        await _loop.ResetAsync();

        UniverseSnapshot s;
        do s = await NextSnapshotAsync(); while (s.Generation != 0 || s.Running);
        Assert.Equal(seed, s.Cells.ToHashSet());
    }

    [Fact]
    public async Task Speed_is_clamped()
    {
        await _loop.SetSpeedAsync(10_000);
        Assert.Equal(SimulationLoop.MaxGenerationsPerSecond, (await NextSnapshotAsync()).GenerationsPerSecond);
        await _loop.SetSpeedAsync(0);
        Assert.Equal(SimulationLoop.MinGenerationsPerSecond, (await NextSnapshotAsync()).GenerationsPerSecond);
    }

    [Fact]
    public async Task A_lower_speed_limit_caps_the_default_and_every_request()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = new SimulationLoop(maxGenerationsPerSecond: 5);
        var run = loop.RunAsync(cts.Token);

        Assert.Equal(5, loop.SpeedLimit);
        Assert.Equal(5, loop.Current.GenerationsPerSecond);

        await loop.SetSpeedAsync(SimulationLoop.MaxGenerationsPerSecond);
        Assert.Equal(5, loop.Current.GenerationsPerSecond);
        await loop.SetSpeedAsync(3);
        Assert.Equal(3, loop.Current.GenerationsPerSecond);

        cts.Cancel();
        await run;
    }

    [Fact]
    public void The_speed_limit_itself_stays_within_the_engine_range()
    {
        Assert.Equal(SimulationLoop.MaxGenerationsPerSecond, new SimulationLoop(maxGenerationsPerSecond: 1000).SpeedLimit);
        Assert.Equal(SimulationLoop.MinGenerationsPerSecond, new SimulationLoop(maxGenerationsPerSecond: 0).SpeedLimit);
    }

    /// <summary>A loop of its own with a frame cap, run for one test.</summary>
    private static async Task WithFrameLimitAsync(int maxFramesPerSecond, Func<SimulationLoop, ChannelReader<UniverseSnapshot>, Task> test)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var published = Channel.CreateUnbounded<UniverseSnapshot>();
        var loop = new SimulationLoop((s, ct) => published.Writer.WriteAsync(s, ct).AsTask(), maxFramesPerSecond: maxFramesPerSecond);
        var run = loop.RunAsync(cts.Token);
        try
        {
            await test(loop, published.Reader);
        }
        finally
        {
            cts.Cancel();
            await run;
        }
    }

    [Fact]
    public Task A_frame_limit_publishes_fewer_frames_than_generations() => WithFrameLimitAsync(10, async (loop, frames) =>
    {
        await loop.LoadAsync(RleParser.Parse(KnownPatterns.Glider));
        await loop.SetSpeedAsync(SimulationLoop.MaxGenerationsPerSecond);
        while (frames.TryRead(out _)) { }
        await loop.StartAsync();
        var started = Stopwatch.GetTimestamp();

        var running = new List<UniverseSnapshot>();
        UniverseSnapshot s;
        do
        {
            s = await frames.ReadAsync();
            if (s.Running && s.Generation > 0) running.Add(s);
        } while (s.Generation < 30);
        var elapsed = Stopwatch.GetElapsedTime(started);

        // At most ten a second (plus the frame that starts the schedule), while the engine ran on
        // at sixty: generations that were never published are the whole point.
        Assert.InRange(running.Count, 1, 10 * elapsed.TotalSeconds + 2);
        Assert.Contains(running.Zip(running.Skip(1)), pair => pair.Second.Generation - pair.First.Generation > 1);
        Assert.True(s.Generation >= 30);
    });

    [Fact]
    public Task Commands_publish_at_once_under_a_frame_limit() => WithFrameLimitAsync(1, async (loop, frames) =>
    {
        await loop.LoadAsync(RleParser.Parse(KnownPatterns.Blinker));
        Assert.Equal(0UL, (await frames.ReadAsync()).Generation);

        // One frame a second would allow only one of these in the next second; each still arrives immediately.
        var started = Stopwatch.GetTimestamp();
        for (var expected = 1UL; expected <= 3; expected++)
        {
            await loop.StepAsync();
            Assert.Equal(expected, loop.Current.Generation);
            Assert.Equal(expected, (await frames.ReadAsync()).Generation);
        }
        Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(900));
    });

    [Fact]
    public Task Pausing_publishes_the_generations_held_back_by_the_frame_limit() => WithFrameLimitAsync(1, async (loop, frames) =>
    {
        await loop.LoadAsync(RleParser.Parse(KnownPatterns.Glider));
        await loop.SetSpeedAsync(SimulationLoop.MaxGenerationsPerSecond);
        await loop.StartAsync();
        await Task.Delay(300); // several generations, at most one of them published
        await loop.PauseAsync();

        var paused = loop.Current;
        Assert.False(paused.Running);
        Assert.True(paused.Generation >= 5, $"only {paused.Generation} generations in 300 ms");

        // The last frame the observer got is the paused one, and nothing follows it.
        UniverseSnapshot last = null!;
        while (frames.TryRead(out var f)) last = f;
        Assert.Same(paused, last);
        await Task.Delay(100);
        Assert.False(frames.TryRead(out _));
    });

    [Fact]
    public void The_frame_limit_must_not_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationLoop(maxFramesPerSecond: -1));
        Assert.Equal(0, new SimulationLoop().FrameRateLimit);
        Assert.Equal(10, new SimulationLoop(maxFramesPerSecond: 10).FrameRateLimit);
    }
}
