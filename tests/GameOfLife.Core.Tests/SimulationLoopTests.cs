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
}
