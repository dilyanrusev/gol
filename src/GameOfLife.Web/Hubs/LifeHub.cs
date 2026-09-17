using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;

namespace GameOfLife.Web.Hubs;

/// <summary>
/// The client's only channel to the simulation. Viewport methods return the client's new frame
/// directly; simulation controls take effect through the loop's broadcast. The public surface is
/// declared by <see cref="ILifeHub"/> so the TypeScript proxy can be generated from it.
/// </summary>
public sealed class LifeHub(SimulationLoop loop, ClientViewports viewports) : Hub<ILifeClient>, ILifeHub
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

    public Task<Frame> Pan(long dx, long dy)
    {
        try
        {
            return Task.FromResult(viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v.Pan(dx, dy))));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public Task<Frame> Resize(int width, int height) =>
        Task.FromResult(viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v.Resize(width, height))));

    public Task<Frame> Recentre() => Task.FromResult(viewports.BuildFrame(viewports.Recentre(Context.ConnectionId)));

    public Task<Frame> Refresh() =>
        Task.FromResult(viewports.BuildFrame(viewports.Update(Context.ConnectionId, v => v)));

    // --- simulation (shared by everyone) ---

    public Task Start() => loop.StartAsync();

    public Task Pause() => loop.PauseAsync();

    public Task Step() => loop.StepAsync();

    public Task Reset() => loop.ResetAsync();

    public Task SetSpeed(int generationsPerSecond) => loop.SetSpeedAsync(generationsPerSecond);
}
