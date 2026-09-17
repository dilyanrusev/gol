using System.Runtime.InteropServices;

namespace GameOfLife.Core;

/// <summary>
/// A sparse Conway's Game of Life universe (rule B3/S23) on a 2^64 x 2^64 torus.
/// Only live cells are stored; a generation costs O(live cells).
/// Not thread-safe: a single owner must perform all mutations.
/// </summary>
public sealed class Universe
{
    private HashSet<Cell> _live = new();
    private HashSet<Cell> _next = new();
    private readonly Dictionary<Cell, int> _neighbourCounts = new();
    // Kept between snapshots so that a steady population indexes without allocating anything but
    // the snapshot itself.
    private readonly SpatialIndex.Builder _indexBuilder = new();

    public ulong Generation { get; private set; }

    public int Population => _live.Count;

    public IReadOnlyCollection<Cell> LiveCells => _live;

    public bool IsAlive(Cell cell) => _live.Contains(cell);

    public void Set(Cell cell, bool alive)
    {
        if (alive) _live.Add(cell);
        else _live.Remove(cell);
    }

    /// <summary>Flips one cell and returns its new state.</summary>
    public bool Toggle(Cell cell)
    {
        if (_live.Remove(cell)) return false;
        _live.Add(cell);
        return true;
    }

    /// <summary>Replaces the population without touching the generation counter (used to undo edits).</summary>
    public void Replace(IEnumerable<Cell> cells)
    {
        _live.Clear();
        _live.UnionWith(cells);
    }

    public void Clear()
    {
        _live.Clear();
        Generation = 0;
    }

    /// <summary>Replaces the whole population and resets the generation counter.</summary>
    public void Load(IEnumerable<Cell> cells)
    {
        _live.Clear();
        _live.UnionWith(cells);
        Generation = 0;
    }

    /// <summary>Places a pattern so that its top-left corner is at <paramref name="topLeft"/>.</summary>
    public void Place(Pattern pattern, Cell topLeft)
    {
        foreach (var (x, y) in pattern.Cells)
            _live.Add(topLeft.Offset(x, y));
    }

    /// <summary>Copies the current population into a new array (a stable snapshot).</summary>
    public Cell[] Snapshot() => _live.ToArray();

    /// <summary>
    /// Copies the current population into a new array grouped by chunk, with the chunk table that
    /// makes viewport projection cheap (see <see cref="SpatialIndex.Builder"/>). Reads the live set
    /// directly, so the grouped array is the only copy made.
    /// </summary>
    public SpatialIndex SnapshotIndexed()
    {
        _indexBuilder.Begin(_live.Count);
        foreach (var cell in _live) _indexBuilder.Add(cell);
        return _indexBuilder.Finish();
    }

    public void Step()
    {
        _neighbourCounts.Clear();
        Span<Cell> neighbours = stackalloc Cell[8];

        foreach (var cell in _live)
        {
            cell.GetNeighbours(neighbours);
            foreach (var n in neighbours)
            {
                ref int count = ref CollectionsMarshal.GetValueRefOrAddDefault(_neighbourCounts, n, out _);
                count++;
            }
        }

        _next.Clear();
        foreach (var (cell, count) in _neighbourCounts)
        {
            // B3/S23: a dead cell with exactly 3 neighbours is born; a live cell with 2 or 3 survives.
            if (count == 3 || (count == 2 && _live.Contains(cell)))
                _next.Add(cell);
        }

        (_live, _next) = (_next, _live);
        Generation++;
    }

    public void Step(int generations)
    {
        for (var i = 0; i < generations; i++) Step();
    }
}
