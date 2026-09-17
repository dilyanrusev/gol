using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// Allocation budgets for the hot paths, from the benchmark baseline in benchmarks/results. Bytes
/// allocated are exact and machine-independent, so unlike timings they can gate the build: a hot
/// path that starts allocating more than its budget fails here rather than showing up as GC time
/// in production. Budgets carry a margin over the measured values; tighten them as the code improves.
/// </summary>
public class AllocationBudgetTests
{
    /// <summary>Random live cells in a square centred on the universe centre (the benchmark's soup).</summary>
    private static Cell[] Soup(int count, int side, int seed)
    {
        var random = new Random(seed);
        var origin = Cell.Centre.Offset(-side / 2, -side / 2);
        var cells = new HashSet<Cell>(count);
        while (cells.Count < count) cells.Add(origin.Offset(random.Next(side), random.Next(side)));
        return cells.ToArray();
    }

    private static Universe Evolved(string rle, int generations)
    {
        var universe = KnownPatterns.UniverseWith(rle);
        universe.Step(generations);
        return universe;
    }

    /// <summary>
    /// Bytes the action allocates in steady state: the minimum over a few runs. The runtime now
    /// and then charges a few kilobytes of its own work (tiering, instrumentation) to a thread,
    /// which made single measurements fail one run in three; an allocation the code itself makes
    /// shows up in every run and so in the minimum.
    /// </summary>
    private static long Allocated(Action action)
    {
        var least = long.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    /// <summary>All benchmark worlds, including the gun, whose population keeps growing.</summary>
    public static TheoryData<string, Universe> Worlds => new()
    {
        { "gun@1000", Evolved(File.ReadAllText(KnownPatterns.GosperGliderGunPath), 1000) },
        { "acorn@6000", Evolved(KnownPatterns.Acorn, 6000) },
        { "soup50k", Load(Soup(50_000, 500, 12345)) },
    };

    /// <summary>
    /// Worlds whose population does not grow: the acorn has settled (it stabilises at generation
    /// 5206), the pulsar oscillates, and the soup only shrinks. A growing population legitimately
    /// makes the engine's sets double their capacity now and then, so the gun is left out here.
    /// </summary>
    public static TheoryData<string, Universe> SteadyWorlds => new()
    {
        { "acorn@6000", Evolved(KnownPatterns.Acorn, 6000) },
        { "pulsar", KnownPatterns.UniverseWith(KnownPatterns.Pulsar) },
        { "soup50k", Load(Soup(50_000, 500, 12345)) },
    };

    private static Universe Load(Cell[] cells)
    {
        var universe = new Universe();
        universe.Load(cells);
        return universe;
    }

    [Theory]
    [MemberData(nameof(SteadyWorlds))]
    public void Stepping_a_steady_population_allocates_nothing(string world, Universe universe)
    {
        universe.Step(); // lets the internal collections grow to the population once
        var bytes = Allocated(() => universe.Step(10));
        Assert.True(bytes == 0, $"{world}: 10 generations allocated {bytes} bytes; the step must reuse its collections");
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void A_snapshot_allocates_only_the_cell_array(string world, Universe universe)
    {
        var bytes = Allocated(() => universe.Snapshot());
        var array = 16L * universe.Population + 64; // 16 bytes per Cell plus the array header, with slack
        Assert.True(bytes <= array, $"{world}: snapshot allocated {bytes} bytes for {universe.Population} cells; budget {array}");
    }

    [Theory]
    [MemberData(nameof(Worlds))]
    public void An_indexed_snapshot_adds_only_its_chunk_table(string world, Universe universe)
    {
        var warm = universe.SnapshotIndexed(); // the reusable chunk bookkeeping grows once
        var bytes = Allocated(() => universe.SnapshotIndexed());
        // The grouped cell array plus a dictionary entry per chunk (about 40 bytes each, with slack).
        var budget = 16L * universe.Population + 64 + 64L * warm.ChunkCount + 512;
        Assert.True(bytes <= budget, $"{world}: indexed snapshot allocated {bytes} bytes for {universe.Population} cells in {warm.ChunkCount} chunks; budget {budget}");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void Projecting_through_the_index_into_a_reused_buffer_allocates_nothing(int size)
    {
        var index = SpatialIndex.Build(Soup(50_000, 500, 12345));
        var viewport = Viewport.CentredOn(Cell.Centre, size, size);
        var buffer = new int[256];
        index.Project(viewport, ref buffer); // grows the buffer once

        var bytes = Allocated(() => index.Project(viewport, ref buffer));

        Assert.True(bytes == 0, $"indexed projection through {size} x {size} into a warm buffer allocated {bytes} bytes");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void Projecting_into_a_reused_buffer_allocates_nothing(int size)
    {
        var soup = Soup(50_000, 500, 12345);
        var viewport = Viewport.CentredOn(Cell.Centre, size, size);
        var buffer = new int[256];
        var count = viewport.Project(soup, ref buffer); // grows the buffer once
        Assert.True(count > 0);

        var bytes = Allocated(() => viewport.Project(soup, ref buffer));

        Assert.True(bytes == 0, $"projecting 50k cells through {size} x {size} into a warm buffer allocated {bytes} bytes");
    }

    [Theory]
    [InlineData(100, 32_000)]  // measured 24.1 KB
    [InlineData(500, 760_000)] // measured 707.8 KB
    public void Projecting_the_soup_through_a_viewport_stays_within_budget(int size, long budget)
    {
        var soup = Soup(50_000, 500, 12345);
        var viewport = Viewport.CentredOn(Cell.Centre, size, size);
        viewport.Project(soup); // warm

        var bytes = Allocated(() => viewport.Project(soup));

        Assert.True(bytes <= budget, $"projecting 50k cells through {size} x {size} allocated {bytes} bytes; budget {budget}");
    }
}
