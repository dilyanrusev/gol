using System.Collections.Concurrent;
using GameOfLife.Core;
using GameOfLife.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// Per-connection viewports. The absolute position lives only here; clients ask for relative changes
/// and receive <see cref="Frame"/>s projected through their own viewport.
/// </summary>
public sealed class ClientViewports(SimulationLoop loop, IHubContext<LifeHub, ILifeClient> hub, ILogger<ClientViewports> logger)
{
    private readonly ConcurrentDictionary<string, Viewport> _viewports = new();

    public int Count => _viewports.Count;

    public Viewport Register(string connectionId)
    {
        var viewport = Viewport.CentredOn(loop.Current.SeedCentre);
        _viewports[connectionId] = viewport;
        return viewport;
    }

    public void Remove(string connectionId) => _viewports.TryRemove(connectionId, out _);

    /// <summary>The connection's viewport, or a default one centred on the seed if it has none yet.</summary>
    public Viewport Get(string connectionId) =>
        _viewports.GetValueOrDefault(connectionId, Viewport.CentredOn(loop.Current.SeedCentre));

    /// <summary>Applies a change to the connection's viewport and returns the new value.</summary>
    public Viewport Update(string connectionId, Func<Viewport, Viewport> change)
    {
        var updated = change(Get(connectionId));
        _viewports[connectionId] = updated;
        return updated;
    }

    public Viewport Recentre(string connectionId) =>
        Update(connectionId, v => Viewport.CentredOn(loop.Current.SeedCentre, v.Width, v.Height));

    public Frame BuildFrame(string connectionId, Viewport viewport) => BuildFrame(connectionId, viewport, loop.Current);

    public Frame BuildFrame(string connectionId, Viewport viewport, UniverseSnapshot snapshot)
    {
        var edit = snapshot.Edit;
        return new Frame(
            snapshot.Generation,
            snapshot.Population,
            snapshot.Running,
            snapshot.GenerationsPerSecond,
            viewport.Width,
            viewport.Height,
            viewport.Project(snapshot.Cells),
            Editing: edit is not null,
            EditingByMe: edit?.Owner == connectionId,
            EditRemainingMs: edit is null ? 0 : ToMilliseconds(edit.Remaining),
            EditTimeoutMs: ToMilliseconds(edit?.Timeout ?? loop.EditTimeout));
    }

    private static int ToMilliseconds(TimeSpan span) => (int)Math.Min(int.MaxValue, Math.Ceiling(span.TotalMilliseconds));

    public async Task BroadcastAsync(UniverseSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_viewports.IsEmpty) return;

        var sends = new List<Task>(_viewports.Count);
        foreach (var (connectionId, viewport) in _viewports)
        {
            var frame = BuildFrame(connectionId, viewport, snapshot);
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
