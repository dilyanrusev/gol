using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Core.Tests;

/// <summary>
/// Well-known patterns in RLE form, taken from the LifeWiki, plus helpers for comparing
/// universes against expected shapes.
/// </summary>
public static class KnownPatterns
{
    public const string Block = "x = 2, y = 2\n2o$2o!";
    public const string Blinker = "x = 3, y = 1\n3o!";
    public const string Toad = "x = 4, y = 2\nb3o$3o!";
    public const string Beacon = "x = 4, y = 4\n2o$2o$2b2o$2b2o!";
    public const string Pulsar =
        "x = 13, y = 13\n2b3o3b3o2b2$o4bobo4bo$o4bobo4bo$o4bobo4bo$2b3o3b3o2b2$2b3o3b3o2b$o4bobo4bo$o4bobo4bo$o4bobo4bo2$2b3o3b3o!";
    public const string Glider = "x = 3, y = 3\nbob$2bo$3o!";
    public const string Lwss = "x = 5, y = 4\nbo2bo$o4b$o3bo$4o!";
    public const string Diehard = "x = 8, y = 3\n6bob$2o6b$bo3b3o!";
    public const string RPentomino = "x = 3, y = 3\nb2o$2o$bo!";
    public const string Acorn = "x = 7, y = 3\nbo5b$3bo3b$2o2b3o!";

    public static string GosperGliderGunPath => Path.Combine(AppContext.BaseDirectory, "patterns", "gosper_glider_gun.rle");

    public static Pattern GosperGliderGun() => RleParser.Parse(File.ReadAllText(GosperGliderGunPath));

    /// <summary>Loads a pattern into a fresh universe with its top-left corner at <paramref name="topLeft"/>.</summary>
    public static Universe UniverseWith(string rle, Cell topLeft)
    {
        var u = new Universe();
        u.Load(RleParser.Parse(rle).ToUniverseCells(topLeft));
        return u;
    }

    public static Universe UniverseWith(string rle) => UniverseWith(rle, Cell.Centre);

    public static HashSet<Cell> Shifted(IEnumerable<Cell> cells, long dx, long dy) =>
        cells.Select(c => c.Offset(dx, dy)).ToHashSet();
}
