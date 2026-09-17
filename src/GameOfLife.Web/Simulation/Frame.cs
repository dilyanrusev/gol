using Tapper;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// What a client sees: the contents of its own viewport. Cells are packed row-major indices
/// (y * Width + x) relative to the viewport, so no absolute coordinate ever reaches the browser.
/// The <c>[TranspilationSource]</c> attribute makes the build emit a matching TypeScript type.
/// </summary>
/// <param name="Cells">The visible live cells, packed by <see cref="GameOfLife.Core.CellsCodec"/>: a tag character and
/// base64, delta-coded indices for sparse views or a bitmap for dense ones. The client decodes it to indices.</param>
/// <param name="Editing">Whether any client holds the edit session.</param>
/// <param name="EditingByMe">Whether the receiving client is the one editing.</param>
/// <param name="EditRemainingMs">Milliseconds until the edit session expires on its own; 0 when nobody is editing.</param>
/// <param name="EditTimeoutMs">The full idle timeout of an edit session, for showing the remaining share.</param>
[TranspilationSource]
public sealed record Frame(
    ulong Generation,
    int Population,
    bool Running,
    int GenerationsPerSecond,
    int Width,
    int Height,
    string Cells,
    bool Editing,
    bool EditingByMe,
    int EditRemainingMs,
    int EditTimeoutMs);
