using System.Text.Json.Serialization.Metadata;
using GameOfLife.Core;
using GameOfLife.Web.Hubs;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GameOfLifeOptions>()
    .Bind(builder.Configuration.GetSection(GameOfLifeOptions.Section))
    .Validate(o => o.IsValid(out _), "The GameOfLife configuration section is invalid.")
    .ValidateOnStart();

// The state directory holds the saved universe and the data-protection key ring (anti-forgery,
// TempData), so both survive a restart. If it cannot be created the app still runs; the keys then
// live wherever the framework puts them by default and the universe stays in memory.
var stateDirectory = builder.Configuration.GetSection(GameOfLifeOptions.Section).Get<GameOfLifeOptions>()?.ResolveStateDirectory()
    ?? GameOfLifeOptions.DefaultStateDirectory();
var keyDirectory = Path.Combine(stateDirectory, "keys");
Exception? stateDirectoryError = null;
try
{
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    stateDirectoryError = ex;
}

builder.Services.AddRazorPages();
// Frames are serialised through compile-time metadata (see WireJsonContext). Touching the resolver
// chain replaces the implicit reflection resolver, so it is added back explicitly for everything
// else (hub method arguments such as the long deltas of Pan).
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    var chain = options.PayloadSerializerOptions.TypeInfoResolverChain;
    chain.Insert(0, WireJsonContext.Default);
    if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
});

builder.Services.AddSingleton<ClientViewports>();
builder.Services.AddSingleton<UniverseStore>();
// The loop is the single writer of the universe. Its observer fans each snapshot out to the
// connected clients. The service is resolved lazily inside the lambda to avoid a construction cycle.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<GameOfLifeOptions>>().Value;
    return new SimulationLoop(
        (snapshot, ct) => sp.GetRequiredService<ClientViewports>().BroadcastAsync(snapshot, ct),
        options.EditTimeout,
        options.MaxGenerationsPerSecond,
        options.MaxFramesPerSecond);
});
builder.Services.AddHostedService<SimulationHostedService>();

var app = builder.Build();

if (stateDirectoryError is not null)
    app.Logger.LogWarning(stateDirectoryError, "Could not create the state directory {Path}; nothing will persist across restarts", stateDirectory);
else
    app.Logger.LogInformation("State directory: {Path}", stateDirectory);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
if (app.Environment.IsDevelopment())
{
    // `npm run watch` writes bundles (with new chunk names) that the build-time manifest below has
    // never seen, so in development the files are served straight from wwwroot.
    app.UseStaticFiles();
}
app.UseRouting();

// The bundles as the build left them: fingerprinted, pre-compressed (gzip at build, gzip and
// Brotli at publish), served with content negotiation and immutable caching, no CPU per request.
// asp-append-version on the pages resolves to the fingerprinted URLs.
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapHub<LifeHub>(LifeHub.Path);

app.Run();

/// <summary>Lets the integration tests host the app through <c>WebApplicationFactory</c>.</summary>
public partial class Program;
