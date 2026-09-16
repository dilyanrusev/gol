namespace GameOfLife.Core;

/// <summary>
/// A client's window onto the universe: an absolute origin (top-left) plus a size in cells.
/// Clients never learn the origin; they only ask for relative changes.
/// </summary>
public readonly record struct Viewport(Cell Origin, int Width, int Height)
{
    public const int MinSize = 5;
    public const int MaxSize = 500;
    public const int DefaultSize = 100;

    /// <summary>Largest relative change a JavaScript client can express exactly (2^53).</summary>
    public const long MaxDelta = 1L << 53;

    public static Viewport CentredOn(Cell centre, int width = DefaultSize, int height = DefaultSize)
    {
        width = ClampSize(width);
        height = ClampSize(height);
        return new Viewport(centre.Offset(-(width / 2), -(height / 2)), width, height);
    }

    public static int ClampSize(int size) => Math.Clamp(size, MinSize, MaxSize);

    public Cell Centre => Origin.Offset(Width / 2, Height / 2);

    /// <summary>Moves the viewport by a relative amount. Throws if the delta is not a JS safe integer.</summary>
    public Viewport Pan(long dx, long dy)
    {
        if (Math.Abs(dx) > MaxDelta || Math.Abs(dy) > MaxDelta)
            throw new ArgumentOutOfRangeException(nameof(dx), $"Viewport deltas must be within ±{MaxDelta}.");
        return this with { Origin = Origin.Offset(dx, dy) };
    }

    /// <summary>Changes the grid size, keeping the centre fixed. Sizes are clamped to the allowed range.</summary>
    public Viewport Resize(int width, int height) => CentredOn(Centre, width, height);

    /// <summary>
    /// Tests whether a cell is visible and, if so, gives its offset from the origin.
    /// Unchecked subtraction makes this correct across the torus seam.
    /// </summary>
    public bool TryProject(Cell cell, out int x, out int y)
    {
        ulong dx = unchecked(cell.X - Origin.X);
        ulong dy = unchecked(cell.Y - Origin.Y);
        if (dx < (ulong)Width && dy < (ulong)Height)
        {
            x = (int)dx;
            y = (int)dy;
            return true;
        }
        x = y = 0;
        return false;
    }

    /// <summary>Visible cells as packed indices (y * Width + x), sorted ascending.</summary>
    public int[] Project(IEnumerable<Cell> cells)
    {
        var result = new List<int>();
        foreach (var c in cells)
            if (TryProject(c, out var x, out var y))
                result.Add(y * Width + x);
        result.Sort();
        return result.ToArray();
    }
}
