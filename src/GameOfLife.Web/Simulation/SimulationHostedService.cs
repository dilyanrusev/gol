using GameOfLife.Core;
using GameOfLife.Core.Rle;
using Microsoft.Extensions.Options;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// Runs the <see cref="SimulationLoop"/> for the lifetime of the app. On start it restores the
/// saved universe, or loads the seed file when there is none; while running it saves the universe
/// at the configured interval and once more at shutdown.
/// </summary>
public sealed class SimulationHostedService(
    SimulationLoop loop,
    UniverseStore store,
    IHostEnvironment environment,
    IOptions<GameOfLifeOptions> options,
    ILogger<SimulationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var run = loop.RunAsync(stoppingToken);

        await RestoreOrSeedAsync(stoppingToken);

        if (options.Value.SaveInterval is { } interval)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                    await store.SaveIfChangedAsync(loop.Current, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        await run;
    }

    /// <summary>Saves before the loop is stopped, so the last generation reaches the disk.</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await store.SaveIfChangedAsync(loop.Current, cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task RestoreOrSeedAsync(CancellationToken cancellationToken)
    {
        if (await store.LoadAsync(cancellationToken) is { } saved)
        {
            await loop.LoadAsync(saved);
            logger.LogInformation("Restored the universe ({Population} cells) from {Path}", saved.Cells.Count, store.FilePath);
            return;
        }

        var seedFile = options.Value.SeedFile;
        var path = Path.IsPathRooted(seedFile) ? seedFile : Path.Combine(environment.ContentRootPath, seedFile);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, seedFile);

        try
        {
            // The seed file is the operator's, not an upload, so the population cap does not apply.
            var pattern = RleParser.Parse(await File.ReadAllTextAsync(path, cancellationToken), maxPopulation: int.MaxValue);
            await loop.LoadAsync(pattern);
            logger.LogInformation("Seeded the universe with {Name} ({Population} cells) from {Path}",
                pattern.Name ?? "an unnamed pattern", pattern.Cells.Count, path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not load the seed file {Path}; starting with an empty universe", path);
        }
    }
}
