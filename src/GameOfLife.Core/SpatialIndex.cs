namespace GameOfLife.Core;

/// <summary>
/// The cells of a snapshot grouped by 64 × 64 chunk, so a viewport can be projected by visiting
/// only the chunks it overlaps instead of the whole population. Built once per snapshot by
/// <see cref="Universe.SnapshotIndexed"/>; immutable afterwards and shared by every client.
/// </summary>
public sealed class SpatialIndex
{
    /// <summary>Chunks are 2^Shift cells on a side.</summary>
    public const int Shift = 6;
    public const int ChunkSize = 1 << Shift;

    /// <summary>Chunk coordinates wrap like cell coordinates do, at 2^64 / ChunkSize.</summary>
    private const ulong KeyMask = (1UL << (64 - Shift)) - 1;

    /// <summary>A chunk's coordinates: the cell coordinates shifted right by <see cref="Shift"/>.</summary>
    public readonly record struct ChunkKey(ulong X, ulong Y);

    private readonly Dictionary<ChunkKey, (int Start, int Length)> _chunks;

    /// <summary>Every live cell, grouped by chunk. Order within a chunk is unspecified.</summary>
    public Cell[] Cells { get; }

    public int ChunkCount => _chunks.Count;

    public static readonly SpatialIndex Empty = new([], new Dictionary<ChunkKey, (int, int)>());

    internal SpatialIndex(Cell[] cells, Dictionary<ChunkKey, (int Start, int Length)> chunks)
    {
        Cells = cells;
        _chunks = chunks;
    }

    public static ChunkKey KeyOf(Cell cell) => new(cell.X >> Shift, cell.Y >> Shift);

    /// <summary>Builds an index from a set of cells with fresh scratch space; for one-off use.</summary>
    public static SpatialIndex Build(ReadOnlySpan<Cell> cells)
    {
        var builder = new Builder();
        builder.Begin(cells.Length);
        foreach (var cell in cells) builder.Add(cell);
        return builder.Finish();
    }

    /// <summary>
    /// Groups cells by chunk: <see cref="Begin"/>, <see cref="Add"/> every cell, <see cref="Finish"/>.
    /// One dictionary lookup per cell, in <see cref="Add"/>, which numbers the chunks in order of
    /// first appearance and records each cell with its chunk's number; <see cref="Finish"/> is then
    /// an array-only counting sort into the grouped array. The scratch space (16 + 4 bytes per cell,
    /// sized to the largest population seen) is kept between uses, so an owner that indexes the
    /// same population generation after generation allocates only the result and its chunk table.
    /// </summary>
    public sealed class Builder
    {
        private readonly Dictionary<ChunkKey, int> _slotOf = new();
        private ChunkKey[] _slotKey = new ChunkKey[16];
        private int[] _slotCursor = new int[16];
        private Cell[] _cells = [];
        private int[] _cellSlot = [];
        private int _slots;
        private int _count;

        /// <summary>Starts a new index of up to <paramref name="capacity"/> cells.</summary>
        public void Begin(int capacity)
        {
            if (_cells.Length < capacity)
            {
                _cells = new Cell[Math.Max(capacity, _cells.Length * 2)];
                _cellSlot = new int[_cells.Length];
            }
            _slotOf.Clear();
            _slots = 0;
            _count = 0;
        }

        /// <summary>Inlined into the caller's loop: a call per cell was measurable on large worlds.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void Add(Cell cell)
        {
            var key = KeyOf(cell);
            ref var slot = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(_slotOf, key, out var known);
            if (!known)
            {
                slot = _slots++;
                if (_slots > _slotKey.Length)
                {
                    Array.Resize(ref _slotKey, _slotKey.Length * 2);
                    Array.Resize(ref _slotCursor, _slotKey.Length);
                }
                _slotKey[slot] = key;
                _slotCursor[slot] = 0;
            }
            _slotCursor[slot]++;
            _cells[_count] = cell;
            _cellSlot[_count] = slot;
            _count++;
        }

        /// <summary>The index of everything added since <see cref="Begin"/>.</summary>
        public SpatialIndex Finish()
        {
            if (_count == 0) return Empty;

            // Counts become start positions; the chunk table gets its ranges.
            var chunks = new Dictionary<ChunkKey, (int Start, int Length)>(_slots);
            var offset = 0;
            for (var slot = 0; slot < _slots; slot++)
            {
                var length = _slotCursor[slot];
                chunks[_slotKey[slot]] = (offset, length);
                _slotCursor[slot] = offset;
                offset += length;
            }

            var grouped = new Cell[_count];
            for (var i = 0; i < _count; i++)
                grouped[_slotCursor[_cellSlot[i]]++] = _cells[i];
            return new SpatialIndex(grouped, chunks);
        }
    }

    /// <summary>The cells of one chunk, or an empty span when the chunk has none.</summary>
    public ReadOnlySpan<Cell> Chunk(ChunkKey key) =>
        _chunks.TryGetValue(key, out var range) ? Cells.AsSpan(range.Start, range.Length) : [];

    /// <summary>
    /// Visible cells as packed indices (y * Width + x), sorted ascending, written into
    /// <paramref name="buffer"/> (grown when too small, otherwise reused; see
    /// <see cref="Viewport.Project(ReadOnlySpan{Cell}, ref int[])"/>). Only the chunks the viewport
    /// overlaps are visited: at most 9 for a 100-cell view, 81 for a 500-cell one, and chunks with
    /// no cells cost a dictionary miss.
    /// </summary>
    public int Project(Viewport viewport, ref int[] buffer)
    {
        var count = 0;
        var firstX = viewport.Origin.X >> Shift;
        var firstY = viewport.Origin.Y >> Shift;
        // The last chunk may lie past the torus seam; the difference is taken in wrapped chunk space.
        var spanX = (unchecked(viewport.Origin.X + (ulong)(viewport.Width - 1)) >> Shift) - firstX & KeyMask;
        var spanY = (unchecked(viewport.Origin.Y + (ulong)(viewport.Height - 1)) >> Shift) - firstY & KeyMask;
        for (ulong dy = 0; dy <= spanY; dy++)
        {
            for (ulong dx = 0; dx <= spanX; dx++)
            {
                var key = new ChunkKey((firstX + dx) & KeyMask, (firstY + dy) & KeyMask);
                foreach (var cell in Chunk(key))
                {
                    if (!viewport.TryProject(cell, out var x, out var y)) continue;
                    if (count == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                    buffer[count++] = y * viewport.Width + x;
                }
            }
        }
        Array.Sort(buffer, 0, count);
        return count;
    }

    /// <summary>Visible cells as packed indices, sorted ascending, in a new array.</summary>
    public int[] Project(Viewport viewport)
    {
        var buffer = new int[256];
        var count = Project(viewport, ref buffer);
        return buffer.AsSpan(0, count).ToArray();
    }
}
