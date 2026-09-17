using System.Threading.Channels;
using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

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
}
