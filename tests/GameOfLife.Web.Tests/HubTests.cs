using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace GameOfLife.Web.Tests;

/// <summary>The hub contract as seen by any SignalR client, without a browser.</summary>
[Collection(WebCollection.Name)]
public sealed class HubTests(WebAppFixture app) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => app.ResetAsync();

    public async Task DisposeAsync()
    {
        await app.Loop.PauseAsync();
        foreach (var connection in _connections) await connection.DisposeAsync();
    }

    /// <summary>A connected client plus every frame it has received, in order.</summary>
    private async Task<(HubConnection Connection, ChannelReader<Frame> Frames)> ConnectAsync()
    {
        var connection = new HubConnectionBuilder().WithUrl(app.HubUrl).Build();
        _connections.Add(connection);
        var frames = Channel.CreateUnbounded<Frame>();
        connection.On<Frame>(nameof(Hubs.ILifeClient.ReceiveFrame), f => frames.Writer.TryWrite(f));
        await connection.StartAsync();
        return (connection, frames.Reader);
    }

    /// <summary>A connected client that also records the progress messages it receives, in order.</summary>
    private async Task<(HubConnection Connection, ChannelReader<Frame> Frames, ChannelReader<(ulong Generation, int Population)> Progress)> ConnectWithProgressAsync()
    {
        var connection = new HubConnectionBuilder().WithUrl(app.HubUrl).Build();
        _connections.Add(connection);
        var frames = Channel.CreateUnbounded<Frame>();
        var progress = Channel.CreateUnbounded<(ulong, int)>();
        connection.On<Frame>(nameof(Hubs.ILifeClient.ReceiveFrame), f => frames.Writer.TryWrite(f));
        connection.On<ulong, int>(nameof(Hubs.ILifeClient.ReceiveProgress), (g, p) => progress.Writer.TryWrite((g, p)));
        await connection.StartAsync();
        return (connection, frames.Reader, progress.Reader);
    }

    private static readonly Pattern Block = Pattern.FromCells([(0, 0), (1, 0), (0, 1), (1, 1)], "Block");

    private static async Task<Frame> NextAsync(ChannelReader<Frame> frames, Func<Frame, bool>? until = null)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (true)
        {
            var frame = await frames.ReadAsync(cts.Token);
            if (until is null || until(frame)) return frame;
        }
    }

    [Fact]
    public async Task Connecting_pushes_the_current_state()
    {
        var (_, frames) = await ConnectAsync();

        var frame = await NextAsync(frames);

        Assert.False(frame.Running);
        Assert.Equal(0UL, frame.Generation);
        Assert.Equal(3, frame.Population);
        Assert.Equal(Viewport.DefaultSize, frame.Width);
        Assert.Equal(Viewport.DefaultSize, frame.Height);
        Assert.Equal(3, CellsCodec.Decode(frame.Cells, frame.Width, frame.Height).Length);
    }

    [Fact]
    public async Task Refresh_returns_the_same_state_the_hub_pushes()
    {
        var (connection, frames) = await ConnectAsync();
        var pushed = await NextAsync(frames);

        var refreshed = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Refresh));

        Assert.Equal(pushed, refreshed);
    }

    [Fact]
    public async Task Resize_is_clamped_to_the_allowed_range()
    {
        var (connection, _) = await ConnectAsync();

        var frame = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Resize), 1, 100_000);

        Assert.Equal(Viewport.MinSize, frame.Width);
        Assert.Equal(Viewport.MaxSize, frame.Height);
    }

    [Fact]
    public async Task Pan_beyond_the_safe_integer_range_is_rejected()
    {
        var (connection, _) = await ConnectAsync();
        var tooFar = (1L << 53) + 1;

        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Pan), tooFar, 0L));

        Assert.True(ex.Message.Contains("Viewport deltas must be within", StringComparison.Ordinal), ex.Message);
        var after = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Refresh));
        Assert.Equal(3, after.Population);
    }

    [Fact]
    public async Task Start_and_pause_are_shared_by_every_client()
    {
        var (first, firstFrames) = await ConnectAsync();
        var (second, secondFrames) = await ConnectAsync();
        await NextAsync(firstFrames);
        await NextAsync(secondFrames);

        await first.InvokeAsync(nameof(Hubs.ILifeHub.Start));
        var running = await NextAsync(secondFrames, f => f.Running && f.Generation > 0);
        Assert.Equal(3, running.Population);

        await second.InvokeAsync(nameof(Hubs.ILifeHub.Pause));
        var paused = await NextAsync(firstFrames, f => !f.Running);
        Assert.True(paused.Generation > 0);
    }

    [Fact]
    public async Task Viewport_changes_affect_only_the_calling_client()
    {
        var (first, _) = await ConnectAsync();
        var (second, _) = await ConnectAsync();

        var resized = await first.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Resize), 20, 30);
        var other = await second.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Refresh));

        Assert.Equal((20, 30), (resized.Width, resized.Height));
        Assert.Equal((Viewport.DefaultSize, Viewport.DefaultSize), (other.Width, other.Height));
    }

    [Fact]
    public async Task Resize_is_capped_by_the_server_limit()
    {
        var (connection, _) = await ConnectAsync();
        var limit = app.Services.GetRequiredService<ClientViewports>().MaxSize;

        var frame = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Resize), limit + 1, limit);

        Assert.Equal(limit, frame.Width);
        Assert.Equal(limit, frame.Height);
    }

    [Fact]
    public async Task The_simulation_pauses_itself_once_nobody_has_watched_for_the_grace_period()
    {
        var (connection, frames) = await ConnectAsync();
        await connection.InvokeAsync(nameof(Hubs.ILifeHub.Start));
        await NextAsync(frames, f => f.Running);

        await connection.DisposeAsync();

        await WaitUntilAsync(() => !app.Loop.Current.Running, WebAppFixture.PauseWhenUnwatched + Timeout);
        Assert.False(app.Loop.Current.Running);
    }

    [Fact]
    public async Task Reconnecting_within_the_grace_period_keeps_the_simulation_running()
    {
        var (first, frames) = await ConnectAsync();
        await first.InvokeAsync(nameof(Hubs.ILifeHub.Start));
        await NextAsync(frames, f => f.Running);

        // A page reload: the old connection goes, the new one arrives well within the grace period.
        await first.DisposeAsync();
        var (_, laterFrames) = await ConnectAsync();
        await NextAsync(laterFrames);

        await Task.Delay(WebAppFixture.PauseWhenUnwatched + TimeSpan.FromSeconds(1));
        Assert.True(app.Loop.Current.Running);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, cts.Token);
        }
    }

    [Fact]
    public async Task A_hidden_client_receives_no_frames_and_is_brought_up_to_date_when_shown()
    {
        var (connection, frames) = await ConnectAsync();
        await connection.InvokeAsync(nameof(Hubs.ILifeHub.Start));
        await NextAsync(frames, f => f.Running);

        var hidden = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.SetVisibility), false);
        Assert.True(hidden.Running);
        // Whatever was already in flight arrives; after that, silence while the world moves on.
        await Task.Delay(200);
        while (frames.TryRead(out _)) { }
        var generationWhenHidden = app.Loop.Current.Generation;
        await Task.Delay(500);
        Assert.False(frames.TryRead(out _));
        Assert.True(app.Loop.Current.Generation > generationWhenHidden, "the simulation should have run on");

        var shown = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.SetVisibility), true);
        Assert.True(shown.Generation > generationWhenHidden);
        var next = await NextAsync(frames);
        Assert.True(next.Generation >= shown.Generation);
    }

    [Fact]
    public async Task Hiding_one_client_does_not_affect_another()
    {
        var (hidden, hiddenFrames) = await ConnectAsync();
        var (_, visibleFrames) = await ConnectAsync();
        await hidden.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.SetVisibility), false);
        await Task.Delay(100);
        while (hiddenFrames.TryRead(out _)) { }

        await hidden.InvokeAsync(nameof(Hubs.ILifeHub.Start));

        var frame = await NextAsync(visibleFrames, f => f.Running && f.Generation >= 3);
        Assert.True(frame.Running);
        Assert.False(hiddenFrames.TryRead(out _));
        Assert.Equal(1, app.Services.GetRequiredService<ClientViewports>().VisibleCount);
    }

    [Fact]
    public async Task A_view_that_does_not_change_gets_progress_instead_of_frames()
    {
        await app.Loop.LoadAsync(Block); // a still life: the view is the same every generation
        var (connection, frames, progress) = await ConnectWithProgressAsync();
        await NextAsync(frames);

        await connection.InvokeAsync(nameof(Hubs.ILifeHub.Start));

        // Running changed, so the first broadcast is a full frame; after that only the counters move.
        var running = await NextAsync(frames, f => f.Running);
        using var cts = new CancellationTokenSource(Timeout);
        var first = await progress.ReadAsync(cts.Token);
        var second = await progress.ReadAsync(cts.Token);
        Assert.True(first.Generation > running.Generation);
        Assert.True(second.Generation > first.Generation);
        Assert.Equal(4, second.Population);
        Assert.False(frames.TryRead(out _));

        // A state change is a frame again, even though the cells are still the same.
        await connection.InvokeAsync(nameof(Hubs.ILifeHub.Pause));
        var paused = await NextAsync(frames, f => !f.Running);
        Assert.Equal(4, CellsCodec.Decode(paused.Cells, paused.Width, paused.Height).Length);
    }

    [Fact]
    public async Task A_view_panned_off_the_pattern_gets_progress_until_it_comes_back()
    {
        var (connection, frames, progress) = await ConnectWithProgressAsync();
        await NextAsync(frames);
        var away = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Pan), 100_000L, 0L);
        Assert.Empty(CellsCodec.Decode(away.Cells, away.Width, away.Height));

        await connection.InvokeAsync(nameof(Hubs.ILifeHub.Start));
        await NextAsync(frames, f => f.Running);
        using var cts = new CancellationTokenSource(Timeout);
        await progress.ReadAsync(cts.Token);
        await progress.ReadAsync(cts.Token);
        Assert.False(frames.TryRead(out _));

        // Back over the blinker, which changes every generation: frames again.
        var back = await connection.InvokeAsync<Frame>(nameof(Hubs.ILifeHub.Recentre));
        Assert.Equal(3, CellsCodec.Decode(back.Cells, back.Width, back.Height).Length);
        var a = await NextAsync(frames, f => f.Generation > back.Generation);
        var b = await NextAsync(frames, f => f.Generation > a.Generation);
        Assert.NotEqual(a.Cells, b.Cells);
    }
}
