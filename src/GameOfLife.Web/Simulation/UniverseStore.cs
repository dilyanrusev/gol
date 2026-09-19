using GameOfLife.Core;
using GameOfLife.Core.Rle;
using Microsoft.Extensions.Options;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// Keeps the universe across restarts as one RLE file in the state directory. The file carries the
/// population's origin, so it reloads in place and becomes the seed at generation 0. Storage
/// trouble (a read-only mount, a corrupt file) is logged and never stops the app: the universe
/// simply starts from the seed file, or lives in memory only.
/// </summary>
public sealed class UniverseStore(IOptions<GameOfLifeOptions> options, ILogger<UniverseStore> logger)
{
    public const string FileName = "universe.rle";

    private UniverseSnapshot? _lastSaved;
    private bool _warnedUnwritable;

    public string Directory { get; } = options.Value.ResolveStateDirectory();

    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>The saved universe, or null when there is none or it cannot be read.</summary>
    public async Task<Pattern?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            // The server wrote this from a universe that fitted in memory, so the upload cap does not apply.
            return RleParser.Parse(await File.ReadAllTextAsync(FilePath, cancellationToken), maxPopulation: int.MaxValue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            logger.LogWarning(ex, "Could not read the saved universe at {Path}; it is ignored", FilePath);
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="snapshot"/> unless it is the one already on disk. Returns whether a
    /// file was written. Never throws: the loop and the host must not depend on the disk.
    /// </summary>
    public async Task<bool> SaveIfChangedAsync(UniverseSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(snapshot, _lastSaved)) return false;

        Pattern pattern;
        try
        {
            pattern = Pattern.FromUniverseCells(snapshot.Cells, "Saved universe") with
            {
                Comments = [$"Saved by the Game of Life server at generation {snapshot.Generation}, population {snapshot.Population}."],
            };
        }
        catch (InvalidOperationException ex)
        {
            // Spread across the torus seam: no pattern can hold it. Keep whatever was saved before.
            logger.LogWarning("Not saving the universe: {Reason}", ex.Message);
            return false;
        }

        var temp = FilePath + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // Write beside the file and swap, so a crash mid-write cannot leave a truncated universe.
            await File.WriteAllTextAsync(temp, RleWriter.Write(pattern), cancellationToken);
            File.Move(temp, FilePath, overwrite: true);
            _lastSaved = snapshot;
            _warnedUnwritable = false;
            logger.LogDebug("Saved the universe (generation {Generation}, {Population} cells) to {Path}", snapshot.Generation, snapshot.Population, FilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_warnedUnwritable)
                logger.LogWarning(ex, "Could not save the universe to {Path}; it will be lost when the server stops", FilePath);
            _warnedUnwritable = true; // one warning per outage, not one per interval
            return false;
        }
    }
}
