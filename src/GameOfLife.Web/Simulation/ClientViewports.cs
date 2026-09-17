using System.Collections.Concurrent;
using GameOfLife.Core;
using GameOfLife.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// Per-connection viewports. The absolute position lives only here; clients ask for relative changes
/// and receive <see cref="Frame"/>s projected through their own viewport.
/// </summary>
/// <remarks>
/// Each connection also owns a projection buffer and an encoding scratch buffer that the broadcast
/// reuses generation after generation: they grow to the largest frame the client has had and are
/// then never allocated again, and they go away with the connection. Only the broadcast may use it: the loop
/// awaits every send before publishing the next generation, so the previous frame has been
/// serialised by the time the buffer is overwritten. Frames returned from hub methods are
/// serialised after the method has returned, with no way to know when, so they get their own arrays.
/// </remarks>
public sealed class ClientViewports(SimulationLoop loop, IHubContext<LifeHub, ILifeClient> hub, ILogger<ClientViewports> logger)
{
    private sealed class Client(Viewport viewport)
    {
        public Viewport Viewport = viewport;
        public int[] Buffer = new int[256];
        public byte[] Scratch = [];
    }

    private readonly ConcurrentDictionary<string, Client> _clients = new();

    public int Count => _clients.Count;

    public Viewport Register(string connectionId)
    {
        var viewport = Viewport.CentredOn(loop.Current.SeedCentre);
        _clients[connectionId] = new Client(viewport);
        return viewport;
    }

    public void Remove(string connectionId) => _clients.TryRemove(connectionId, out _);

    /// <summary>The connection's viewport, or a default one centred on the seed if it has none yet.</summary>
    public Viewport Get(string connectionId) =>
        _clients.TryGetValue(connectionId, out var client) ? client.Viewport : Viewport.CentredOn(loop.Current.SeedCentre);

    /// <summary>Applies a change to the connection's viewport and returns the new value.</summary>
    public Viewport Update(string connectionId, Func<Viewport, Viewport> change)
    {
        var client = _clients.GetOrAdd(connectionId, _ => new Client(Viewport.CentredOn(loop.Current.SeedCentre)));
        client.Viewport = change(client.Viewport);
        return client.Viewport;
    }

    public Viewport Recentre(string connectionId) =>
        Update(connectionId, v => Viewport.CentredOn(loop.Current.SeedCentre, v.Width, v.Height));

    public Frame BuildFrame(string connectionId, Viewport viewport) => BuildFrame(connectionId, viewport, loop.Current);

    public Frame BuildFrame(string connectionId, Viewport viewport, UniverseSnapshot snapshot) =>
        BuildFrame(connectionId, viewport, snapshot, CellsCodec.Encode(snapshot.Index.Project(viewport), viewport.Width, viewport.Height));

    private Frame BuildFrame(string connectionId, Viewport viewport, UniverseSnapshot snapshot, string cells)
    {
        var edit = snapshot.Edit;
        return new Frame(
            snapshot.Generation,
            snapshot.Population,
            snapshot.Running,
            snapshot.GenerationsPerSecond,
            viewport.Width,
            viewport.Height,
            cells,
            Editing: edit is not null,
            EditingByMe: edit?.Owner == connectionId,
            EditRemainingMs: edit is null ? 0 : ToMilliseconds(edit.Remaining),
            EditTimeoutMs: ToMilliseconds(edit?.Timeout ?? loop.EditTimeout));
    }

    private static int ToMilliseconds(TimeSpan span) => (int)Math.Min(int.MaxValue, Math.Ceiling(span.TotalMilliseconds));

    public async Task BroadcastAsync(UniverseSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_clients.IsEmpty) return;

        var sends = new List<Task>(_clients.Count);
        foreach (var (connectionId, client) in _clients)
        {
            var viewport = client.Viewport;
            var count = snapshot.Index.Project(viewport, ref client.Buffer);
            var cells = CellsCodec.Encode(client.Buffer.AsSpan(0, count), viewport.Width, viewport.Height, ref client.Scratch);
            var frame = BuildFrame(connectionId, viewport, snapshot, cells);
            sends.Add(hub.Clients.Client(connectionId).ReceiveFrame(frame, cancellationToken));
        }

        try
        {
            await Task.WhenAll(sends);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to deliver a frame to at least one client");
        }
    }
}
