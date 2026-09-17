namespace GameOfLife.Core;

/// <summary>
/// An immutable view of the simulation at one instant. Produced by the simulation loop after
/// every change and safe to read from any thread.
/// </summary>
/// <param name="Index">The population, grouped by chunk so that every client's viewport can be projected cheaply.</param>
/// <param name="Edit">The active edit session, if a client is hand-editing the universe.</param>
public sealed record UniverseSnapshot(
    ulong Generation,
    SpatialIndex Index,
    bool Running,
    int GenerationsPerSecond,
    Cell SeedCentre,
    EditSession? Edit = null)
{
    /// <summary>Every live cell, in chunk order.</summary>
    public Cell[] Cells => Index.Cells;

    public int Population => Cells.Length;
}
