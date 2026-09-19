using GameOfLife.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GameOfLife.Web.Tests;

/// <summary>
/// One real server and one headless browser for the whole collection. The app runs on Kestrel at a
/// random loopback port (not the in-memory test server, which a browser cannot reach), so the
/// tests exercise the same HTTP, WebSocket and static-file paths as production.
/// </summary>
public sealed class WebAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>A period-2 oscillator: three cells in a row. Population stays 3, shape flips every generation.</summary>
    public static readonly Pattern Blinker = Pattern.FromCells([(0, 0), (1, 0), (2, 0)], "Blinker");

    public IBrowser Browser { get; private set; } = null!;
    private IPlaywright _playwright = null!;

    public Uri BaseAddress { get; private set; } = null!;

    public Uri HubUrl => new(BaseAddress, Hubs.LifeHub.Path);

    public SimulationLoop Loop => Services.GetRequiredService<SimulationLoop>();

    /// <summary>Short enough for a browser test to watch an edit session run out, long enough not to expire mid-test.</summary>
    public static readonly TimeSpan EditTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long the simulation runs on with nobody connected before pausing itself. Short enough
    /// to test, long enough that the gap between two tests' connections never trips it.
    /// </summary>
    public static readonly TimeSpan PauseWhenUnwatched = TimeSpan.FromSeconds(2);

    /// <summary>The server's state directory: a fresh temporary folder, so the tests never touch the developer's saved universe.</summary>
    public string StateDirectory { get; } = Path.Combine(Path.GetTempPath(), "game-of-life-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        builder.UseSetting("GameOfLife:EditTimeoutSeconds", EditTimeout.TotalSeconds.ToString(invariant));
        builder.UseSetting("GameOfLife:PauseWhenUnwatchedSeconds", PauseWhenUnwatched.TotalSeconds.ToString(invariant));
        builder.UseSetting("GameOfLife:StateDirectory", StateDirectory);

        // Full exception text in hub errors, so a failing test says what went wrong on the server.
        builder.ConfigureServices(services => services.Configure<HubOptions>(o => o.EnableDetailedErrors = true));

        // Server logs interleave with the test output, so keep only genuine errors. SignalR logs every
        // exception a hub method throws at Error level, including the HubExceptions some tests provoke
        // on purpose; those already reach the test in full, so the dispatcher's entries are dropped.
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Error);
            logging.AddFilter("Microsoft.AspNetCore.SignalR.Internal.DefaultHubDispatcher", LogLevel.None);
        });
    }

    public async Task InitializeAsync()
    {
        UseKestrel(0);
        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not report its addresses.");
        BaseAddress = new Uri(addresses.Addresses.First());

        // Uses the Chromium build that ships with this Playwright version, for reproducible rendering
        // across machines. The install is a no-op once the browser is cached.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>
    /// Puts the shared simulation into a known state: the blinker, generation 0, paused, nobody editing.
    /// A previous test's editor may still be disconnecting, so its session is released here rather
    /// than waited for.
    /// </summary>
    public async Task ResetAsync()
    {
        if (Loop.Current.Edit is { } edit) await Loop.ReleaseEditAsync(edit.Owner);
        await Loop.LoadAsync(Blinker);
    }

    /// <summary>A page in its own context, at Playwright's default 1280 x 720 unless a viewport is given.</summary>
    public Task<IPage> NewPageAsync(ViewportSize? viewport = null) => Browser.NewPageAsync(new()
    {
        BaseURL = BaseAddress.ToString(),
        ViewportSize = viewport,
    });

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public override async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        await base.DisposeAsync();
        try
        {
            Directory.Delete(StateDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Left behind in the temp folder; nothing to do about it here.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class WebCollection : ICollectionFixture<WebAppFixture>
{
    public const string Name = "web";
}
