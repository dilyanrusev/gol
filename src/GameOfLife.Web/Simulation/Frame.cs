using Tapper;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// What a client sees: the contents of its own viewport. Cells are packed row-major indices
/// (y * Width + x) relative to the viewport, so no absolute coordinate ever reaches the browser.
/// The <c>[TranspilationSource]</c> attribute makes the build emit a matching TypeScript type.
/// </summary>
[TranspilationSource]
public sealed record Frame(
    ulong Generation,
    int Population,
    bool Running,
    int GenerationsPerSecond,
    int Width,
    int Height,
    int[] Cells);
