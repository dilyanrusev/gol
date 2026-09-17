using System.Text.Json.Serialization.Metadata;
using GameOfLife.Core;
using GameOfLife.Web.Hubs;
using GameOfLife.Web.Simulation;

var builder = WebApplication.CreateBuilder(args);

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
// The loop is the single writer of the universe. Its observer fans each snapshot out to the
// connected clients. GameOfLife:EditTimeoutSeconds bounds how long an idle edit session may hold it.
// The service is resolved lazily inside the lambda to avoid a construction cycle.
builder.Services.AddSingleton(sp => new SimulationLoop(
    (snapshot, ct) => sp.GetRequiredService<ClientViewports>().BroadcastAsync(snapshot, ct),
    editTimeout: TimeSpan.FromSeconds(builder.Configuration.GetValue(
        "GameOfLife:EditTimeoutSeconds", SimulationLoop.DefaultEditTimeout.TotalSeconds))));
builder.Services.AddHostedService<SimulationHostedService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// Plain static files (not MapStaticAssets) so that `npm run watch` output appears without a rebuild.
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapHub<LifeHub>(LifeHub.Path);

app.Run();

/// <summary>Lets the integration tests host the app through <c>WebApplicationFactory</c>.</summary>
public partial class Program;
