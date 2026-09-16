namespace GameOfLife.Core;

/// <summary>
/// A finite pattern with cells expressed relative to its own top-left corner.
/// This is the in-memory form of an RLE file and the bridge between files and the universe.
/// </summary>
public sealed record Pattern
{
    public const int MaxDimension = 1 << 20;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required IReadOnlyList<(int X, int Y)> Cells { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string> Comments { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Absolute universe position of the top-left corner, when known (set when a pattern is
    /// extracted from a universe, and restored from the "origin" comment on load).
    /// </summary>
    public Cell? Origin { get; init; }

    public static Pattern Empty { get; } = new() { Width = 0, Height = 0, Cells = Array.Empty<(int, int)>() };

    public static Pattern FromCells(IEnumerable<(int X, int Y)> cells, string? name = null)
    {
        var list = cells.Distinct().OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        if (list.Length == 0) return Empty with { Name = name };
        int minX = list.Min(c => c.X), minY = list.Min(c => c.Y);
        int maxX = list.Max(c => c.X), maxY = list.Max(c => c.Y);
        var normalised = list.Select(c => (c.X - minX, c.Y - minY)).ToArray();
        return new Pattern
        {
            Width = maxX - minX + 1,
            Height = maxY - minY + 1,
            Cells = normalised,
            Name = name,
        };
    }

    /// <summary>
    /// Builds a pattern from absolute universe cells using their bounding box.
    /// Throws if the population is spread over more than <see cref="MaxDimension"/> cells in
    /// either direction (including a population that straddles the torus seam).
    /// </summary>
    public static Pattern FromUniverseCells(IReadOnlyCollection<Cell> cells, string? name = null)
    {
        if (cells.Count == 0) return Empty with { Name = name };

        ulong minX = ulong.MaxValue, minY = ulong.MaxValue, maxX = 0, maxY = 0;
        foreach (var c in cells)
        {
            if (c.X < minX) minX = c.X;
            if (c.X > maxX) maxX = c.X;
            if (c.Y < minY) minY = c.Y;
            if (c.Y > maxY) maxY = c.Y;
        }

        ulong spanX = maxX - minX, spanY = maxY - minY;
        if (spanX >= MaxDimension || spanY >= MaxDimension)
            throw new InvalidOperationException(
                $"The population spans {spanX + 1} x {spanY + 1} cells, which exceeds the {MaxDimension} limit for a pattern file.");

        var relative = cells
            .Select(c => ((int)(c.X - minX), (int)(c.Y - minY)))
            .OrderBy(c => c.Item2).ThenBy(c => c.Item1)
            .ToArray();

        return new Pattern
        {
            Width = (int)spanX + 1,
            Height = (int)spanY + 1,
            Cells = relative,
            Name = name,
            Origin = new Cell(minX, minY),
        };
    }

    /// <summary>The top-left corner that centres this pattern on <paramref name="centre"/>.</summary>
    public Cell TopLeftWhenCentredAt(Cell centre) => centre.Offset(-(Width / 2), -(Height / 2));

    /// <summary>Absolute cells for this pattern when its top-left corner is at <paramref name="topLeft"/>.</summary>
    public IEnumerable<Cell> ToUniverseCells(Cell topLeft) => Cells.Select(c => topLeft.Offset(c.X, c.Y));
}
