using GameOfLife.Core;
using GameOfLife.Core.Rle;
using static GameOfLife.Core.Tests.KnownPatterns;

namespace GameOfLife.Core.Tests;

/// <summary>Compares the engine against documented behaviour of well-known Life patterns.</summary>
public class UniverseTests
{
    [Fact]
    public void Empty_universe_stays_empty()
    {
        var u = new Universe();
        u.Step(10);
        Assert.Equal(0, u.Population);
        Assert.Equal(10UL, u.Generation);
    }

    [Fact]
    public void Lone_cell_dies()
    {
        var u = new Universe();
        u.Set(Cell.Centre, true);
        u.Step();
        Assert.Equal(0, u.Population);
    }

    [Fact]
    public void Block_is_a_still_life()
    {
        var u = UniverseWith(Block);
        var start = u.LiveCells.ToHashSet();
        u.Step(25);
        Assert.Equal(start, u.LiveCells.ToHashSet());
    }

    [Theory]
    [InlineData(Blinker, 2)]
    [InlineData(Toad, 2)]
    [InlineData(Beacon, 2)]
    [InlineData(Pulsar, 3)]
    public void Oscillators_return_to_their_start_after_one_period_and_not_before(string rle, int period)
    {
        var u = UniverseWith(rle);
        var start = u.LiveCells.ToHashSet();

        for (var g = 1; g < period; g++)
        {
            u.Step();
            Assert.NotEqual(start, u.LiveCells.ToHashSet());
        }

        u.Step();
        Assert.Equal(start, u.LiveCells.ToHashSet());
    }

    [Fact]
    public void Blinker_phases_are_exactly_horizontal_then_vertical()
    {
        var u = UniverseWith(Blinker, new Cell(10, 10));
        u.Step();
        Assert.Equal(new HashSet<Cell> { new(11, 9), new(11, 10), new(11, 11) }, u.LiveCells.ToHashSet());
        u.Step();
        Assert.Equal(new HashSet<Cell> { new(10, 10), new(11, 10), new(12, 10) }, u.LiveCells.ToHashSet());
    }

    [Fact]
    public void Glider_moves_one_cell_diagonally_every_four_generations()
    {
        var u = UniverseWith(Glider);
        var start = u.LiveCells.ToHashSet();

        u.Step(4);
        Assert.Equal(Shifted(start, 1, 1), u.LiveCells.ToHashSet());

        u.Step(4 * 99);
        Assert.Equal(Shifted(start, 100, 100), u.LiveCells.ToHashSet());
    }

    [Fact]
    public void Lightweight_spaceship_moves_two_cells_left_every_four_generations()
    {
        var u = UniverseWith(Lwss);
        var start = u.LiveCells.ToHashSet();
        u.Step(4);
        Assert.Equal(Shifted(start, -2, 0), u.LiveCells.ToHashSet());
    }

    [Fact]
    public void Glider_crosses_the_torus_seam_in_both_axes()
    {
        // Start 5 cells before the seam; after 4 * 8 generations the glider has moved by (8, 8)
        // and must reappear near (0, 0), exactly as if the universe were infinite.
        var topLeft = new Cell(ulong.MaxValue - 4, ulong.MaxValue - 4);
        var u = UniverseWith(Glider, topLeft);
        var start = u.LiveCells.ToHashSet();

        u.Step(32);

        var expected = Shifted(start, 8, 8);
        Assert.Equal(expected, u.LiveCells.ToHashSet());
        Assert.Contains(u.LiveCells, c => c.X < 8 && c.Y < 8);
    }

    [Fact]
    public void Neighbours_wrap_across_the_seam()
    {
        // A blinker centred on (0, 0) spans (ulong.MaxValue, 0), (0, 0), (1, 0).
        var u = UniverseWith(Blinker, new Cell(ulong.MaxValue, 0));
        u.Step();
        Assert.Equal(new HashSet<Cell> { new(0, ulong.MaxValue), new(0, 0), new(0, 1) }, u.LiveCells.ToHashSet());
    }

    [Fact]
    public void Diehard_dies_after_exactly_130_generations()
    {
        var u = UniverseWith(Diehard);
        u.Step(129);
        Assert.True(u.Population > 0);
        u.Step();
        Assert.Equal(0, u.Population);
    }

    [Fact]
    public void R_pentomino_stabilises_at_generation_1103_with_population_116()
    {
        var u = UniverseWith(RPentomino);
        u.Step(1103);
        Assert.Equal(116, u.Population);
        // Everything left is still lifes, blinkers and gliders, so the population stays put.
        u.Step(10);
        Assert.Equal(116, u.Population);
    }

    [Fact]
    public void Acorn_stabilises_at_generation_5206_with_population_633()
    {
        var u = UniverseWith(Acorn);
        u.Step(5206);
        Assert.Equal(633, u.Population);
        u.Step(4);
        Assert.Equal(633, u.Population);
    }

    [Fact]
    public void Gosper_glider_gun_emits_a_glider_every_30_generations()
    {
        var u = new Universe();
        var gun = GosperGliderGun();
        u.Place(gun, gun.TopLeftWhenCentredAt(Cell.Centre));
        var gunCells = u.LiveCells.ToHashSet();
        Assert.Equal(36, u.Population);

        for (var k = 1; k <= 10; k++)
        {
            u.Step(30);
            Assert.Equal(36 + 5 * k, u.Population);
            Assert.True(gunCells.IsSubsetOf(u.LiveCells), $"gun not back in phase after {30 * k} generations");
        }
    }

    [Fact]
    public void Load_replaces_population_and_resets_generation()
    {
        var u = UniverseWith(Glider);
        u.Step(7);
        u.Load(RleParser.Parse(Block).ToUniverseCells(Cell.Centre));
        Assert.Equal(0UL, u.Generation);
        Assert.Equal(4, u.Population);
    }

    [Fact]
    public void Toggle_flips_a_cell_and_reports_its_new_state()
    {
        var u = new Universe();
        var c = new Cell(5, 5);

        Assert.True(u.Toggle(c));
        Assert.True(u.IsAlive(c));
        Assert.False(u.Toggle(c));
        Assert.False(u.IsAlive(c));
        Assert.Equal(0, u.Population);
    }

    [Fact]
    public void Replace_swaps_the_population_but_keeps_the_generation()
    {
        var u = new Universe();
        u.Load([new Cell(1, 0), new Cell(1, 1), new Cell(1, 2)]);
        u.Step();

        u.Replace([new Cell(9, 9)]);

        Assert.Equal(1UL, u.Generation);
        Assert.Equal(new[] { new Cell(9, 9) }, u.Snapshot());
    }
}
