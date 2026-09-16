using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Core.Tests;

public class RleWriterTests
{
    [Fact]
    public void Writes_glider_in_canonical_form()
    {
        var text = RleWriter.Write(RleParser.Parse(KnownPatterns.Glider));
        Assert.Equal("x = 3, y = 3, rule = B3/S23\nbo$2bo$3o!\n", text);
    }

    [Fact]
    public void Writes_header_comments_and_origin()
    {
        var p = new Pattern
        {
            Width = 1, Height = 1, Cells = new[] { (0, 0) },
            Name = "Dot", Comments = new[] { "hello" }, Origin = new Cell(5, ulong.MaxValue),
        };

        var text = RleWriter.Write(p);

        Assert.Equal("#N Dot\n#C hello\n#C origin 5 18446744073709551615\nx = 1, y = 1, rule = B3/S23\no!\n", text);
    }

    [Fact]
    public void Compresses_empty_rows_and_trailing_dead_cells()
    {
        var p = new Pattern { Width = 5, Height = 5, Cells = new[] { (0, 0), (2, 4) } };
        Assert.Equal("x = 5, y = 5, rule = B3/S23\no4$2bo!\n", RleWriter.Write(p));
    }

    [Fact]
    public void Writes_empty_pattern()
    {
        Assert.Equal("x = 0, y = 0, rule = B3/S23\n!\n", RleWriter.Write(Pattern.Empty));
    }

    [Fact]
    public void Wraps_long_bodies_at_70_columns()
    {
        // A checkerboard row of 200 cells never compresses, so it must wrap.
        var cells = Enumerable.Range(0, 100).Select(i => (2 * i + 1, 0)).ToArray();
        var text = RleWriter.Write(new Pattern { Width = 200, Height = 1, Cells = cells });

        var bodyLines = text.Split('\n').Skip(1).Where(l => l.Length > 0).ToArray();
        Assert.True(bodyLines.Length > 1);
        Assert.All(bodyLines, l => Assert.True(l.Length <= RleWriter.MaxLineLength, $"line too long: {l.Length}"));
        Assert.Equal(cells, RleParser.Parse(text).Cells);
    }

    [Theory]
    [InlineData(KnownPatterns.Block)]
    [InlineData(KnownPatterns.Blinker)]
    [InlineData(KnownPatterns.Toad)]
    [InlineData(KnownPatterns.Beacon)]
    [InlineData(KnownPatterns.Pulsar)]
    [InlineData(KnownPatterns.Glider)]
    [InlineData(KnownPatterns.Lwss)]
    [InlineData(KnownPatterns.Diehard)]
    [InlineData(KnownPatterns.RPentomino)]
    [InlineData(KnownPatterns.Acorn)]
    public void Round_trips_known_patterns(string rle)
    {
        var original = RleParser.Parse(rle);
        var reparsed = RleParser.Parse(RleWriter.Write(original));

        Assert.Equal(original.Cells, reparsed.Cells);
        Assert.Equal(original.Width, reparsed.Width);
        Assert.Equal(original.Height, reparsed.Height);
    }

    [Fact]
    public void Round_trips_gosper_glider_gun_through_a_universe()
    {
        var gun = KnownPatterns.GosperGliderGun();
        var topLeft = new Cell(1000, 2000);
        var universe = new Universe();
        universe.Place(gun, topLeft);

        var extracted = Pattern.FromUniverseCells(universe.Snapshot(), gun.Name);
        var reloaded = RleParser.Parse(RleWriter.Write(extracted));

        Assert.Equal(topLeft, reloaded.Origin);
        Assert.Equal(gun.Cells, reloaded.Cells);
        Assert.Equal(gun.Name, reloaded.Name);
        Assert.Equal(universe.LiveCells.ToHashSet(), reloaded.ToUniverseCells(reloaded.Origin!.Value).ToHashSet());
    }

    [Fact]
    public void Refuses_to_extract_a_population_that_straddles_the_seam()
    {
        var cells = new[] { new Cell(0, 0), new Cell(ulong.MaxValue, 0) };
        Assert.Throws<InvalidOperationException>(() => Pattern.FromUniverseCells(cells));
    }
}
