using GameOfLife.Core;
using GameOfLife.Core.Rle;

namespace GameOfLife.Web.Simulation;

/// <summary>Runs the <see cref="SimulationLoop"/> for the lifetime of the app and loads the initial seed.</summary>
public sealed class SimulationHostedService(
    SimulationLoop loop,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<SimulationHostedService> logger) : BackgroundService
{
    public const string SeedFileSetting = "GameOfLife:SeedFile";
    public const string DefaultSeedFile = "patterns/gosper_glider_gun.rle";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var run = loop.RunAsync(stoppingToken);

        var seedFile = configuration[SeedFileSetting] ?? DefaultSeedFile;
        var path = Path.IsPathRooted(seedFile) ? seedFile : Path.Combine(environment.ContentRootPath, seedFile);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, seedFile);

        try
        {
            var pattern = RleParser.Parse(await File.ReadAllTextAsync(path, stoppingToken));
            await loop.LoadAsync(pattern);
            logger.LogInformation("Seeded the universe with {Name} ({Population} cells) from {Path}",
                pattern.Name ?? "an unnamed pattern", pattern.Cells.Count, path);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not load the seed file {Path}; starting with an empty universe", path);
        }

        await run;
    }
}
