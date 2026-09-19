using GameOfLife.Core;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// The <c>GameOfLife</c> configuration section. Every value has a default that suits a developer
/// machine; <c>appsettings.Production.json</c> lowers the limits for a small shared host, and any of
/// them can be overridden with an environment variable such as <c>GameOfLife__MaxGenerationsPerSecond</c>.
/// </summary>
public sealed class GameOfLifeOptions
{
    public const string Section = "GameOfLife";
    public const string DefaultSeedFile = "patterns/gosper_glider_gun.rle";

    /// <summary>The pattern the server starts with when it has no saved universe. Relative paths are resolved against the content root.</summary>
    public string SeedFile { get; set; } = DefaultSeedFile;

    /// <summary>How long an idle edit session lasts before it ends on its own.</summary>
    public double EditTimeoutSeconds { get; set; } = SimulationLoop.DefaultEditTimeout.TotalSeconds;

    /// <summary>The fastest speed a client may ask for; at most <see cref="SimulationLoop.MaxGenerationsPerSecond"/>.</summary>
    public int MaxGenerationsPerSecond { get; set; } = SimulationLoop.MaxGenerationsPerSecond;

    /// <summary>
    /// The most frames per second clients receive while the simulation runs faster than that;
    /// the generations in between are computed but not sent. 0 sends every generation. This is
    /// the bandwidth knob; <see cref="MaxGenerationsPerSecond"/> is the CPU one.
    /// </summary>
    public int MaxFramesPerSecond { get; set; }

    /// <summary>The largest grid a client may ask for, in cells per side; at most <see cref="Viewport.MaxSize"/>.</summary>
    public int MaxViewportSize { get; set; } = Viewport.MaxSize;

    /// <summary>The most live cells an uploaded or hand-written pattern may have.</summary>
    public int MaxPopulation { get; set; } = Pattern.MaxPopulation;

    /// <summary>
    /// How long the simulation keeps running with no client connected before it pauses itself,
    /// so a closed tab does not leave the server stepping for nobody. A page reload reconnects
    /// well within it. Zero or less never pauses.
    /// </summary>
    public double PauseWhenUnwatchedSeconds { get; set; } = 60;

    /// <summary>How often the universe is written to <see cref="StateDirectory"/> while it changes. Zero or less saves only at shutdown.</summary>
    public double SaveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Where the universe and the data-protection key ring are kept between runs. Defaults to
    /// <c>dilyanrusev/game-of-life</c> under the user's local application data
    /// (<c>~/.local/share</c> on Linux, <c>%LOCALAPPDATA%</c> on Windows).
    /// </summary>
    public string? StateDirectory { get; set; }

    public TimeSpan EditTimeout => TimeSpan.FromSeconds(EditTimeoutSeconds);

    public TimeSpan? PauseWhenUnwatched => PauseWhenUnwatchedSeconds > 0 ? TimeSpan.FromSeconds(PauseWhenUnwatchedSeconds) : null;

    public TimeSpan? SaveInterval => SaveIntervalSeconds > 0 ? TimeSpan.FromSeconds(SaveIntervalSeconds) : null;

    /// <summary>The configured state directory, or the default one, as an absolute path. Nothing is created.</summary>
    public string ResolveStateDirectory() =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(StateDirectory) ? DefaultStateDirectory() : StateDirectory);

    public static string DefaultStateDirectory()
    {
        // DoNotVerify: the folder may not exist yet (a fresh container), and it is created on first use.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath(); // no HOME at all
        return Path.Combine(local, "dilyanrusev", "game-of-life");
    }

    public bool IsValid(out string? error)
    {
        error = null;
        if (EditTimeoutSeconds <= 0) error = "EditTimeoutSeconds must be positive.";
        else if (MaxGenerationsPerSecond < SimulationLoop.MinGenerationsPerSecond || MaxGenerationsPerSecond > SimulationLoop.MaxGenerationsPerSecond)
            error = $"MaxGenerationsPerSecond must be between {SimulationLoop.MinGenerationsPerSecond} and {SimulationLoop.MaxGenerationsPerSecond}.";
        else if (MaxViewportSize < Viewport.MinSize || MaxViewportSize > Viewport.MaxSize)
            error = $"MaxViewportSize must be between {Viewport.MinSize} and {Viewport.MaxSize}.";
        else if (MaxFramesPerSecond < 0) error = "MaxFramesPerSecond must be 0 (every generation) or positive.";
        else if (MaxPopulation <= 0) error = "MaxPopulation must be positive.";
        return error is null;
    }
}
