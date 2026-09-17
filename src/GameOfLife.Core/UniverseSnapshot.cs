namespace GameOfLife.Core;

/// <summary>
/// An immutable view of the simulation at one instant. Produced by the simulation loop after
/// every change and safe to read from any thread.
/// </summary>
/// <param name="Edit">The active edit session, if a client is hand-editing the universe.</param>
public sealed record UniverseSnapshot(
    ulong Generation,
    Cell[] Cells,
    bool Running,
    int GenerationsPerSecond,
    Cell SeedCentre,
    EditSession? Edit = null)
{
    public int Population => Cells.Length;
}
