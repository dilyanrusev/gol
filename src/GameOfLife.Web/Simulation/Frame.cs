using MessagePack;
using Tapper;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// What a client sees: the contents of its own viewport. Cells are packed row-major indices
/// (y * Width + x) relative to the viewport, so no absolute coordinate ever reaches the browser.
/// The <c>[TranspilationSource]</c> attribute makes the build emit a matching TypeScript type.
/// The browser talks MessagePack (the keys below are the wire names, and the cells travel as raw
/// bytes); the JSON protocol stays available for other clients and base64-encodes the cells.
/// Two frames are equal when their fields are, cells byte for byte.
/// </summary>
/// <param name="Cells">The visible live cells, packed by <see cref="GameOfLife.Core.CellsCodec"/>: a tag byte, then
/// delta-coded indices for sparse views or a bitmap for dense ones. The client decodes it to indices.</param>
/// <param name="Editing">Whether any client holds the edit session.</param>
/// <param name="EditingByMe">Whether the receiving client is the one editing.</param>
/// <param name="EditRemainingMs">Milliseconds until the edit session expires on its own; 0 when nobody is editing.</param>
/// <param name="EditTimeoutMs">The full idle timeout of an edit session, for showing the remaining share.</param>
[TranspilationSource]
[MessagePackObject]
public sealed record Frame(
    [property: Key("generation")] ulong Generation,
    [property: Key("population")] int Population,
    [property: Key("running")] bool Running,
    [property: Key("generationsPerSecond")] int GenerationsPerSecond,
    [property: Key("width")] int Width,
    [property: Key("height")] int Height,
    [property: Key("cells")] byte[] Cells,
    [property: Key("editing")] bool Editing,
    [property: Key("editingByMe")] bool EditingByMe,
    [property: Key("editRemainingMs")] int EditRemainingMs,
    [property: Key("editTimeoutMs")] int EditTimeoutMs)
{
    public bool Equals(Frame? other) =>
        other is not null
        && Generation == other.Generation
        && Population == other.Population
        && Running == other.Running
        && GenerationsPerSecond == other.GenerationsPerSecond
        && Width == other.Width
        && Height == other.Height
        && Cells.AsSpan().SequenceEqual(other.Cells)
        && Editing == other.Editing
        && EditingByMe == other.EditingByMe
        && EditRemainingMs == other.EditRemainingMs
        && EditTimeoutMs == other.EditTimeoutMs;

    public override int GetHashCode() => HashCode.Combine(Generation, Population, Running, Width, Height, Cells.Length);
}
