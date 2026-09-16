using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Core.Tests;

public class RleParserTests
{
    [Fact]
    public void Parses_glider()
    {
        var p = RleParser.Parse(KnownPatterns.Glider);

        Assert.Equal(3, p.Width);
        Assert.Equal(3, p.Height);
        Assert.Equal(new[] { (1, 0), (2, 1), (0, 2), (1, 2), (2, 2) }, p.Cells);
    }

    [Fact]
    public void Parses_gosper_glider_gun_example_file()
    {
        var p = KnownPatterns.GosperGliderGun();

        Assert.Equal("Gosper glider gun", p.Name);
        Assert.Equal(36, p.Width);
        Assert.Equal(9, p.Height);
        Assert.Equal(36, p.Cells.Count);
        Assert.Contains("Author: Bill Gosper", p.Comments);
        Assert.Null(p.Origin);
        // The two blocks that anchor the gun.
        Assert.Contains((0, 4), p.Cells);
        Assert.Contains((1, 5), p.Cells);
        Assert.Contains((34, 2), p.Cells);
        Assert.Contains((35, 3), p.Cells);
    }

    [Theory]
    [InlineData("x = 3, y = 1\n3o!")]
    [InlineData("x = 3, y = 1, rule = B3/S23\n3o!")]
    [InlineData("x = 3, y = 1, rule = b3/s23\n3o!")]
    [InlineData("x = 3, y = 1, rule = 23/3\n3o!")]
    [InlineData("x=3,y=1,rule=B3/S23\n3o!")]
    [InlineData("  x = 3 , y = 1 , rule = B3/S23  \n3o!")]
    public void Accepts_header_variants_for_conway_rule(string rle)
    {
        var p = RleParser.Parse(rle);
        Assert.Equal(3, p.Cells.Count);
    }

    [Theory]
    [InlineData("x = 3, y = 1, rule = B36/S23\n3o!")]
    [InlineData("x = 3, y = 1, rule = B2/S\n3o!")]
    public void Rejects_other_rules(string rle)
    {
        var ex = Assert.Throws<FormatException>(() => RleParser.Parse(rle));
        Assert.Contains("rule", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_missing_header()
    {
        Assert.Throws<FormatException>(() => RleParser.Parse("3o!"));
    }

    [Fact]
    public void Rejects_unexpected_characters()
    {
        Assert.Throws<FormatException>(() => RleParser.Parse("x = 3, y = 1\n3o?!"));
    }

    [Fact]
    public void Handles_run_counts_on_row_breaks_and_wrapped_lines()
    {
        // Same as "o3$o" but split across lines with whitespace, and no trailing '!'.
        var p = RleParser.Parse("x = 1, y = 4\no3\n$o\n");

        Assert.Equal(new[] { (0, 0), (0, 3) }, p.Cells);
        Assert.Equal(4, p.Height);
    }

    [Fact]
    public void Ignores_content_after_terminator()
    {
        var p = RleParser.Parse("x = 1, y = 1\no!\nthis is not RLE and must be ignored");
        Assert.Single(p.Cells);
    }

    [Fact]
    public void Treats_other_letters_as_live_cells()
    {
        var p = RleParser.Parse("x = 2, y = 1\nAo!");
        Assert.Equal(2, p.Cells.Count);
    }

    [Fact]
    public void Grows_dimensions_when_body_exceeds_header()
    {
        var p = RleParser.Parse("x = 1, y = 1\n3o$o!");
        Assert.Equal(3, p.Width);
        Assert.Equal(2, p.Height);
    }

    [Fact]
    public void Reads_origin_comment_and_keeps_other_comments()
    {
        var p = RleParser.Parse("#N Test\n#C first\n#C origin 18446744073709551615 42\n#C second\nx = 1, y = 1\no!");

        Assert.Equal("Test", p.Name);
        Assert.Equal(new Cell(ulong.MaxValue, 42), p.Origin);
        Assert.Equal(new[] { "first", "second" }, p.Comments);
    }

    [Fact]
    public void Empty_body_gives_empty_pattern()
    {
        var p = RleParser.Parse("x = 0, y = 0\n!");
        Assert.Empty(p.Cells);
        Assert.Equal(0, p.Width);
    }
}
