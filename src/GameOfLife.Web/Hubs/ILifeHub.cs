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

    /// <summary>Runs the simulation for everyone. Refused while a client is editing.</summary>
    Task Start();

    Task Pause();

    /// <summary>Advances one generation. Refused while a client is editing.</summary>
    Task Step();

    /// <summary>Restores the seed at generation 0. Refused while a client is editing.</summary>
    Task Reset();

    Task SetSpeed(int generationsPerSecond);

    /// <summary>
    /// Takes the exclusive edit session for this client. Requires a paused simulation and no other
    /// editor. Calling it again while already editing restarts the idle timeout.
    /// </summary>
    Task<Frame> BeginEdit();

    /// <summary>Flips the cell at viewport position (x, y). Only the editing client may call it.</summary>
    Task<Frame> ToggleCell(int x, int y);

    /// <summary>Ends this client's edit session, keeping the edits, and resumes the simulation.</summary>
    Task<Frame> EndEdit();

    /// <summary>Ends this client's edit session and restores the universe as it was when editing began. Stays paused.</summary>
    Task<Frame> CancelEdit();
}
