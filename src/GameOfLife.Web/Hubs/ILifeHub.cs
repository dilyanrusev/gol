using GameOfLife.Web.Simulation;
using TypedSignalR.Client;

namespace GameOfLife.Web.Hubs;

/// <summary>
/// Methods a client can invoke on the hub. Together with <see cref="ILifeClient"/> and the
/// <c>[TranspilationSource]</c> types this is the source of the generated TypeScript proxy in
/// <c>Scripts/generated</c> (see the <c>GenerateSignalRClient</c> target in the project file).
/// </summary>
[Hub]
public interface ILifeHub
{
    /// <summary>Moves this client's viewport by whole cells. Deltas beyond ±2^53 are rejected.</summary>
    Task<Frame> Pan(long dx, long dy);

    /// <summary>Sets the grid size in cells (clamped to the allowed range), keeping the centre.</summary>
    Task<Frame> Resize(int width, int height);

    /// <summary>Moves this client's viewport back onto the centre of the seed pattern.</summary>
    Task<Frame> Recentre();

    /// <summary>Returns the current frame (used to initialise the page and after reconnects).</summary>
    Task<Frame> Refresh();

    Task Start();

    Task Pause();

    Task Step();

    Task Reset();

    Task SetSpeed(int generationsPerSecond);
}
