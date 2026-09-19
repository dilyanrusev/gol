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
        await Clients.Caller.ReceiveFrame(viewports.BuildFrame(Context.ConnectionId, viewport));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        viewports.Remove(Context.ConnectionId);
        try
        {
            // A departing editor keeps its edits but must not keep the lock.
            await loop.ReleaseEditAsync(Context.ConnectionId);
        }
        catch (InvalidOperationException)
        {
            // The loop has stopped (application shutdown); nothing left to release.
        }
        await base.OnDisconnectedAsync(exception);
    }

    private Frame CurrentFrame() => viewports.BuildFrame(Context.ConnectionId, viewports.Get(Context.ConnectionId));

    // --- viewport (per connection) ---

    public Task<Frame> Pan(long dx, long dy)
    {
        try
        {
            return Task.FromResult(viewports.BuildFrame(Context.ConnectionId, viewports.Update(Context.ConnectionId, v => v.Pan(dx, dy))));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    public Task<Frame> Resize(int width, int height) =>
        Task.FromResult(viewports.BuildFrame(Context.ConnectionId, viewports.Resize(Context.ConnectionId, width, height)));

    public Task<Frame> Recentre() =>
        Task.FromResult(viewports.BuildFrame(Context.ConnectionId, viewports.Recentre(Context.ConnectionId)));

    public Task<Frame> Refresh() => Task.FromResult(CurrentFrame());

    // --- simulation (shared by everyone) ---

    public Task Start() => Guarded(loop.StartAsync());

    public Task Pause() => loop.PauseAsync();

    public Task Step() => Guarded(loop.StepAsync());

    public Task Reset() => Guarded(loop.ResetAsync());

    public Task SetSpeed(int generationsPerSecond) => loop.SetSpeedAsync(generationsPerSecond);

    // --- editing (exclusive) ---

    public async Task<Frame> BeginEdit()
    {
        await Guarded(loop.BeginEditAsync(Context.ConnectionId));
        return CurrentFrame();
    }

    public async Task<Frame> ToggleCell(int x, int y)
    {
        var viewport = viewports.Get(Context.ConnectionId);
        if (x < 0 || y < 0 || x >= viewport.Width || y >= viewport.Height)
            throw new HubException($"({x}, {y}) is outside the {viewport.Width} x {viewport.Height} viewport.");

        await Guarded(loop.ToggleCellAsync(Context.ConnectionId, viewport.Origin.Offset(x, y)));
        return CurrentFrame();
    }

    public async Task<Frame> EndEdit()
    {
        await Guarded(loop.EndEditAsync(Context.ConnectionId, resume: true));
        return CurrentFrame();
    }

    public async Task<Frame> CancelEdit()
    {
        await Guarded(loop.CancelEditAsync(Context.ConnectionId));
        return CurrentFrame();
    }

    /// <summary>
    /// Turns the loop's refusals into messages the client can show. A refusal caused by the caller's
    /// own edit session gets a hint to finish editing instead of the generic "someone is editing".
    /// </summary>
    private async Task Guarded(Task command)
    {
        try
        {
            await command;
        }
        catch (EditInProgressException ex)
        {
            var mine = loop.Current.Edit?.Owner == Context.ConnectionId;
            throw new HubException(mine ? "You are editing the universe. Press Done or Cancel first." : ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
