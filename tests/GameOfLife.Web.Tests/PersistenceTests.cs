using GameOfLife.Core;
using GameOfLife.Core.Rle;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameOfLife.Web.Tests;

/// <summary>
/// The universe survives a restart through the state directory. These tests host their own
/// in-memory server per case (no browser), each with its own temporary state directory.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), "game-of-life-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private UniverseStore NewStore(string? directory = null) =>
        new(Options.Create(new GameOfLifeOptions { StateDirectory = directory ?? _stateDirectory }), NullLogger<UniverseStore>.Instance);

    private static UniverseSnapshot SnapshotOf(IEnumerable<Cell> cells, ulong generation = 0)
    {
        var universe = new Universe();
        universe.Load(cells);
        return new UniverseSnapshot(generation, universe.SnapshotIndexed(), false, 10, Cell.Centre);
    }

    private WebApplicationFactory<Program> NewServer() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("GameOfLife:StateDirectory", _stateDirectory);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Error));
    });

    /// <summary>The hosted service seeds asynchronously after the host starts, so the first state is awaited.</summary>
    private static async Task<UniverseSnapshot> PopulatedAsync(SimulationLoop loop)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (loop.Current.Population == 0)
            await Task.Delay(20, cts.Token);
        return loop.Current;
    }

    [Fact]
    public async Task Save_creates_the_directory_and_the_file_reloads_in_place()
    {
        var store = NewStore();
        var cells = new[] { new Cell(1000, 2000), new Cell(1001, 2000), new Cell(1002, 2000) };

        Assert.True(await store.SaveIfChangedAsync(SnapshotOf(cells, generation: 42)));
        Assert.True(File.Exists(store.FilePath));

        var restored = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(new Cell(1000, 2000), restored.Origin);
        Assert.Equal(cells.ToHashSet(), restored.ToUniverseCells(restored.Origin!.Value).ToHashSet());
        Assert.Contains(restored.Comments, c => c.Contains("generation 42"));
    }

    [Fact]
    public async Task Saving_the_same_snapshot_twice_writes_once()
    {
        var store = NewStore();
        var snapshot = SnapshotOf([Cell.Centre]);

        Assert.True(await store.SaveIfChangedAsync(snapshot));
        Assert.False(await store.SaveIfChangedAsync(snapshot));
        Assert.True(await store.SaveIfChangedAsync(SnapshotOf([Cell.Centre], generation: 1)));
    }

    [Fact]
    public async Task An_unwritable_directory_is_reported_not_thrown()
    {
        // A directory cannot be created underneath a file.
        Directory.CreateDirectory(_stateDirectory);
        var file = Path.Combine(_stateDirectory, "not-a-directory");
        await File.WriteAllTextAsync(file, "");
        var store = NewStore(Path.Combine(file, "state"));

        Assert.False(await store.SaveIfChangedAsync(SnapshotOf([Cell.Centre])));
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_corrupt_file_is_ignored()
    {
        var store = NewStore();
        Directory.CreateDirectory(_stateDirectory);
        await File.WriteAllTextAsync(store.FilePath, "this is not RLE");

        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task The_server_restores_the_saved_universe_instead_of_the_seed_file()
    {
        var saved = Pattern.FromUniverseCells([new Cell(500, 600), new Cell(501, 600), new Cell(502, 600)], "Saved");
        Directory.CreateDirectory(_stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stateDirectory, UniverseStore.FileName), RleWriter.Write(saved));

        await using var server = NewServer();
        var snapshot = await PopulatedAsync(server.Services.GetRequiredService<SimulationLoop>());

        Assert.Equal(3, snapshot.Population);
        Assert.Equal(new HashSet<Cell> { new(500, 600), new(501, 600), new(502, 600) }, snapshot.Cells.ToHashSet());
        Assert.Equal(new Cell(501, 600), snapshot.SeedCentre);
        Assert.False(snapshot.Running);
    }

    [Fact]
    public async Task Stopping_the_server_saves_the_universe_for_the_next_start()
    {
        Cell[] atShutdown;
        await using (var server = NewServer())
        {
            var loop = server.Services.GetRequiredService<SimulationLoop>();
            await PopulatedAsync(loop); // the seed file, since nothing is saved yet
            await loop.StepAsync();
            await loop.StepAsync();
            atShutdown = loop.Current.Cells;
            Assert.Equal(2UL, loop.Current.Generation);
        }

        var restored = await NewStore().LoadAsync(CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(atShutdown.ToHashSet(), restored.ToUniverseCells(restored.Origin!.Value).ToHashSet());

        // And a second server picks it up, at generation 0 with the saved population as its seed.
        await using var next = NewServer();
        var snapshot = await PopulatedAsync(next.Services.GetRequiredService<SimulationLoop>());
        Assert.Equal(atShutdown.ToHashSet(), snapshot.Cells.ToHashSet());
        Assert.Equal(0UL, snapshot.Generation);
    }
}
