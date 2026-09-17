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
}
