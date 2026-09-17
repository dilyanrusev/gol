using System.Text.Json;
using System.Text.Json.Serialization;
using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Benchmarks;

/// <summary>
/// Realistic universes for the benchmarks. Small patterns hide the O(population) costs, so the
/// set spans a few dozen cells (the gun early on), several hundred (the acorn mid-life) and tens
/// of thousands (a random soup).
/// </summary>
public static class Workloads
{
    public const string Acorn = "x = 7, y = 3\nbo5b$3bo3b$2o2b3o!";

    public static readonly string[] Names = ["gun@1000", "acorn@5000", "soup50k"];

    /// <summary>The live cells of the named world, ready to be loaded into a fresh universe.</summary>
    public static Cell[] Cells(string name) => name switch
    {
        "gun@1000" => Evolve(GosperGliderGun(), 1000),
        "acorn@5000" => Evolve(RleParser.Parse(Acorn), 5000),
        "soup50k" => Soup(50_000, 500, seed: 12345),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown world."),
    };

    public static Pattern GosperGliderGun() =>
        RleParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "patterns", "gosper_glider_gun.rle")));

    private static Cell[] Evolve(Pattern pattern, int generations)
    {
        var universe = new Universe();
        universe.Load(pattern.ToUniverseCells(pattern.TopLeftWhenCentredAt(Cell.Centre)));
        universe.Step(generations);
        return universe.Snapshot();
    }

    /// <summary>Random live cells in a square of <paramref name="side"/> cells centred on the universe centre.</summary>
    public static Cell[] Soup(int count, int side, int seed)
    {
        var random = new Random(seed);
        var origin = Cell.Centre.Offset(-side / 2, -side / 2);
        var cells = new HashSet<Cell>(count);
        while (cells.Count < count)
            cells.Add(origin.Offset(random.Next(side), random.Next(side)));
        return cells.ToArray();
    }

    /// <summary>A viewport of the given size centred on the universe centre, as clients get by default.</summary>
    public static Viewport CentredViewport(int size) => Viewport.CentredOn(Cell.Centre, size, size);

    /// <summary>Mirrors GameOfLife.Web.Simulation.Frame, so the wire cost can be measured without ASP.NET.</summary>
    public sealed record WireFrame(
        ulong Generation, int Population, bool Running, int GenerationsPerSecond, int Width, int Height, int[] Cells,
        bool Editing, bool EditingByMe, int EditRemainingMs, int EditTimeoutMs);

    /// <summary>What SignalR's JSON protocol does with a frame: camel-cased System.Text.Json to UTF-8.</summary>
    public static readonly JsonSerializerOptions WireJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
