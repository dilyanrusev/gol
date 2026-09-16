namespace GameOfLife.Core;

/// <summary>
/// A position in the 2^64 x 2^64 toroidal universe.
/// Coordinates are unsigned; arithmetic on them is unchecked, so wrapping at the
/// edges of the torus comes for free from ulong overflow.
/// </summary>
public readonly record struct Cell(ulong X, ulong Y)
{
    /// <summary>The centre of the universe. Seeds are placed around this point.</summary>
    public static readonly Cell Centre = new(1UL << 63, 1UL << 63);

    public Cell Offset(long dx, long dy) => unchecked(new Cell(X + (ulong)dx, Y + (ulong)dy));

    /// <summary>The eight Moore neighbours, wrapping at the torus edges.</summary>
    public void GetNeighbours(Span<Cell> buffer)
    {
        if (buffer.Length < 8) throw new ArgumentException("Buffer must hold 8 cells.", nameof(buffer));
        unchecked
        {
            ulong xm = X - 1, xp = X + 1, ym = Y - 1, yp = Y + 1;
            buffer[0] = new Cell(xm, ym);
            buffer[1] = new Cell(X, ym);
            buffer[2] = new Cell(xp, ym);
            buffer[3] = new Cell(xm, Y);
            buffer[4] = new Cell(xp, Y);
            buffer[5] = new Cell(xm, yp);
            buffer[6] = new Cell(X, yp);
            buffer[7] = new Cell(xp, yp);
        }
    }

    public override string ToString() => $"({X}, {Y})";
}
