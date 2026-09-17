using GameOfLife.Web.Simulation;

namespace GameOfLife.Web.Hubs;

/// <summary>
/// Methods the server can call on a connected client. The method name is the SignalR message name
/// the browser subscribes to (<c>connection.on("ReceiveFrame", ...)</c> in <c>viewer.ts</c>).
/// </summary>
public interface ILifeClient
{
    /// <summary>Delivers the client's view of the universe after every change.</summary>
    Task ReceiveFrame(Frame frame, CancellationToken cancellationToken = default);
}
