using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;

namespace GameOfLife.Web.Hubs;

/// <summary>
/// The client's only channel to the simulation. Viewport methods return the client's new frame
/// directly; simulation controls take effect through the loop's broadcast.
/// </summary>
public sealed class LifeHub(SimulationLoop loop, ClientViewports viewports) : Hub<ILifeClient>
{
    public const string Path = "/hubs/life";

    public override async Task OnConnectedAsync()
    {
        var viewport = viewports.Register(Context.ConnectionId);
        await Clients.Caller.ReceiveFrame(viewports.BuildFrame(viewport));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        viewports.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    // --- viewport (per connection) ---

    /// <summary>Moves this client's viewport by whole cells. Deltas beyond ±2^53 are rejected.</summary>
    public Frame Pan(long dx, long dy)
    {
        try
        {
            return viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v.Pan(dx, dy)));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    /// <summary>Sets the grid size in cells (clamped to the allowed range), keeping the centre.</summary>
    public Frame Resize(int width, int height) =>
        viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v.Resize(width, height)));

    /// <summary>Moves this client's viewport back onto the centre of the seed pattern.</summary>
    public Frame Recentre() => viewports.BuildFrame(viewports.Recentre(Context.ConnectionId));

    /// <summary>Re-sends the current frame (used after reconnects).</summary>
    public Frame Refresh() =>
        viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v));

    // --- simulation (shared by everyone) ---

    public Task Start() => loop.StartAsync();

    public Task Pause() => loop.PauseAsync();

    public Task Step() => loop.StepAsync();

    public Task Reset() => loop.ResetAsync();

    public Task SetSpeed(int generationsPerSecond) => loop.SetSpeedAsync(generationsPerSecond);
}
