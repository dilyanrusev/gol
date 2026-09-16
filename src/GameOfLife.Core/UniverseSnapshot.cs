namespace GameOfLife.Core;

/// <summary>
/// An immutable view of the simulation at one instant. Produced by the simulation loop after
/// every change and safe to read from any thread.
/// </summary>
public sealed record UniverseSnapshot(
    ulong Generation,
    Cell[] Cells,
    bool Running,
    int GenerationsPerSecond,
    Cell SeedCentre)
{
    public int Population => Cells.Length;
}
