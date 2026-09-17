using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>The chunk index projects exactly what the flat walk projects, including across the torus seam.</summary>
public class SpatialIndexTests
{
    private static Cell[] Soup(int count, Cell origin, int side, int seed)
    {
        var random = new Random(seed);
        var cells = new HashSet<Cell>(count);
        while (cells.Count < count) cells.Add(origin.Offset(random.Next(side), random.Next(side)));
        return cells.ToArray();
    }

    [Fact]
    public void The_index_holds_every_cell_grouped_by_chunk()
    {
        var cells = Soup(5000, Cell.Centre, 300, 1);

        var index = SpatialIndex.Build(cells);

        Assert.Equal(cells.ToHashSet(), index.Cells.ToHashSet());
        var seen = new HashSet<SpatialIndex.ChunkKey>();
        SpatialIndex.ChunkKey? current = null;
        foreach (var cell in index.Cells)
        {
            var key = SpatialIndex.KeyOf(cell);
            if (key != current) { Assert.True(seen.Add(key), "a chunk appears twice"); current = key; }
        }
        Assert.Equal(seen.Count, index.ChunkCount);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(500, 500)]
    [InlineData(5, 7)]
    public void Projection_through_the_index_matches_the_flat_walk_for_random_viewports(int width, int height)
    {
        var cells = Soup(20_000, Cell.Centre, 400, 2);
        var index = SpatialIndex.Build(cells);
        var random = new Random(3);

        for (var i = 0; i < 25; i++)
        {
            var origin = Cell.Centre.Offset(random.Next(-500, 500), random.Next(-500, 500));
            var viewport = new Viewport(origin, width, height);
            Assert.Equal(viewport.Project(cells), index.Project(viewport));
        }
    }

    [Fact]
    public void Projection_is_right_across_the_torus_seam_and_across_chunk_edges()
    {
        // Cells on both sides of the wrap in both axes, and straddling the chunk boundary at 64.
        var origin = new Cell(ulong.MaxValue - 40, ulong.MaxValue - 40);
        // 80 of the 100 cells in the 10 x 10 square at (60, 60): more than the area would hang the generator.
        var cells = Soup(3000, origin, 120, 4).Concat(Soup(80, new Cell(60, 60), 10, 5)).Distinct().ToArray();
        var index = SpatialIndex.Build(cells);

        foreach (var viewport in new[]
        {
            new Viewport(origin, 100, 100),                              // seam inside the view
            new Viewport(new Cell(ulong.MaxValue - 3, 0), 10, 10),       // seam in X only
            new Viewport(new Cell(0, ulong.MaxValue - 3), 10, 10),       // seam in Y only
            new Viewport(new Cell(60, 60), 12, 12),                      // chunk edge at 64 inside the view
            new Viewport(new Cell(63, 63), 5, 5),                        // view starting on the last cell of a chunk
        })
        {
            var expected = viewport.Project(cells);
            Assert.NotEmpty(expected);
            Assert.Equal(expected, index.Project(viewport));
        }
    }

    [Fact]
    public void An_empty_index_projects_nothing()
    {
        Assert.Empty(SpatialIndex.Empty.Project(Viewport.CentredOn(Cell.Centre)));
        Assert.Empty(SpatialIndex.Build([]).Project(Viewport.CentredOn(Cell.Centre)));
    }

    [Fact]
    public void The_engines_indexed_snapshot_has_the_same_cells_as_its_flat_one()
    {
        var universe = KnownPatterns.UniverseWith(KnownPatterns.Pulsar);
        universe.Step(2);

        var index = universe.SnapshotIndexed();

        Assert.Equal(universe.Snapshot().ToHashSet(), index.Cells.ToHashSet());
        Assert.Equal(universe.Population, index.Cells.Length);
    }
}
