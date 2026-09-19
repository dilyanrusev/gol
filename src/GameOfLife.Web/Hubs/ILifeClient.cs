using GameOfLife.Web.Simulation;
using TypedSignalR.Client;

namespace GameOfLife.Web.Hubs;

/// <summary>
/// Methods the server can call on a connected client. The generated TypeScript receiver in
/// <c>Scripts/generated</c> subscribes to these by name.
/// </summary>
[Receiver]
public interface ILifeClient
{
    /// <summary>Delivers the client's view of the universe after every change.</summary>
    Task ReceiveFrame(Frame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces <see cref="ReceiveFrame"/> when nothing in the client's view changed (a still life,
    /// empty space) and neither did the running or editing state: only the counters moved on. A
    /// fraction of a frame's size, and no cells to decode.
    /// </summary>
    Task ReceiveProgress(ulong generation, int population, CancellationToken cancellationToken = default);
}
