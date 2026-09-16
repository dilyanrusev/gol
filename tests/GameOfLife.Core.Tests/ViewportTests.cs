using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

public class ViewportTests
{
    [Fact]
    public void Centred_viewport_puts_centre_cell_in_the_middle()
    {
        var v = Viewport.CentredOn(new Cell(1000, 1000), 100, 50);

        Assert.Equal(new Cell(950, 975), v.Origin);
        Assert.Equal(new Cell(1000, 1000), v.Centre);
        Assert.True(v.TryProject(new Cell(1000, 1000), out var x, out var y));
        Assert.Equal((50, 25), (x, y));
    }

    [Fact]
    public void Projects_cells_across_the_seam()
    {
        var v = new Viewport(new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1), 4, 4);

        Assert.True(v.TryProject(new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1), out var x0, out var y0));
        Assert.Equal((0, 0), (x0, y0));

        Assert.True(v.TryProject(new Cell(1, 1), out var x1, out var y1));
        Assert.Equal((3, 3), (x1, y1));

        Assert.False(v.TryProject(new Cell(2, 0), out _, out _));
        Assert.False(v.TryProject(new Cell(ulong.MaxValue - 2, 0), out _, out _));
    }

    [Fact]
    public void Project_packs_visible_cells_as_sorted_row_major_indices()
    {
        var v = new Viewport(new Cell(10, 10), 5, 5);
        var cells = new[] { new Cell(14, 14), new Cell(10, 10), new Cell(12, 11), new Cell(9, 9), new Cell(15, 10) };

        Assert.Equal(new[] { 0, 7, 24 }, v.Project(cells));
    }

    [Fact]
    public void Pan_wraps_and_accepts_negative_deltas()
    {
        var v = new Viewport(new Cell(1, 1), 10, 10);
        var moved = v.Pan(-3, -3);
        Assert.Equal(new Cell(ulong.MaxValue - 1, ulong.MaxValue - 1), moved.Origin);
        Assert.Equal(v, moved.Pan(3, 3));
    }

    [Theory]
    [InlineData((1L << 53) + 1, 0)]
    [InlineData(0, -((1L << 53) + 1))]
    [InlineData(long.MaxValue, 0)]
    [InlineData(long.MinValue + 1, 0)]
    public void Pan_rejects_deltas_outside_javascript_safe_integers(long dx, long dy)
    {
        var v = Viewport.CentredOn(Cell.Centre);
        Assert.Throws<ArgumentOutOfRangeException>(() => v.Pan(dx, dy));
    }

    [Fact]
    public void Pan_accepts_the_largest_safe_delta()
    {
        var v = Viewport.CentredOn(Cell.Centre);
        var moved = v.Pan(Viewport.MaxDelta, -Viewport.MaxDelta);
        Assert.Equal(Cell.Centre.Offset(-50, -50).Offset(Viewport.MaxDelta, -Viewport.MaxDelta), moved.Origin);
    }

    [Fact]
    public void Resize_clamps_and_keeps_the_centre()
    {
        var v = Viewport.CentredOn(Cell.Centre, 100, 100);

        var big = v.Resize(10_000, 3);
        Assert.Equal(Viewport.MaxSize, big.Width);
        Assert.Equal(Viewport.MinSize, big.Height);
        Assert.Equal(v.Centre, big.Centre);

        var same = v.Resize(200, 200).Resize(100, 100);
        Assert.Equal(v, same);
    }
}
