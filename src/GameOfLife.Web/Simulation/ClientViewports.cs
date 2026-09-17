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

    /// <summary>Applies a change to the connection's viewport and returns the new value.</summary>
    public Viewport Update(string connectionId, Func<Viewport, Viewport> change)
    {
        var current = _viewports.GetValueOrDefault(connectionId, Viewport.CentredOn(loop.Current.SeedCentre));
        var updated = change(current);
        _viewports[connectionId] = updated;
        return updated;
    }

    public Viewport Recentre(string connectionId) =>
        Update(connectionId, v => Viewport.CentredOn(loop.Current.SeedCentre, v.Width, v.Height));

    public Frame BuildFrame(Viewport viewport) => BuildFrame(viewport, loop.Current);

    public static Frame BuildFrame(Viewport viewport, UniverseSnapshot snapshot) => new(
        snapshot.Generation,
        snapshot.Population,
        snapshot.Running,
        snapshot.GenerationsPerSecond,
        viewport.Width,
        viewport.Height,
        viewport.Project(snapshot.Cells));

    public async Task BroadcastAsync(UniverseSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_viewports.IsEmpty) return;

        var sends = new List<Task>(_viewports.Count);
        foreach (var (connectionId, viewport) in _viewports)
        {
            var frame = BuildFrame(viewport, snapshot);
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
