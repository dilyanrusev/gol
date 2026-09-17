using System.Text.Json;
using BenchmarkDotNet.Attributes;
using GameOfLife.Core;
using static GameOfLife.Benchmarks.Workloads;

namespace GameOfLife.Benchmarks;

/// <summary>The engine: one generation, and copying the population out for a snapshot.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class UniverseBenchmarks
{
    private const int StepsPerInvoke = 20;

    [Params("gun@1000", "acorn@5000", "soup50k")]
    public string World { get; set; } = "";

    private Cell[] _cells = [];
    private readonly Universe _universe = new();

    [GlobalSetup]
    public void LoadWorld() => _cells = Cells(World);

    // Stepping mutates the universe; reloading per iteration keeps every measurement on the same world
    // (within StepsPerInvoke generations of drift).
    [IterationSetup]
    public void Reload() => _universe.Load(_cells);

    [Benchmark(OperationsPerInvoke = StepsPerInvoke)]
    public void Step()
    {
        for (var i = 0; i < StepsPerInvoke; i++) _universe.Step();
    }

    [Benchmark]
    public Cell[] Snapshot() => _universe.Snapshot();
}

/// <summary>Per client, per generation: projecting the population through a viewport into packed indices.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class ViewportBenchmarks
{
    [Params(100, 500)]
    public int Size { get; set; }

    private Cell[] _soup = [];
    private Viewport _viewport;

    [GlobalSetup]
    public void Setup()
    {
        _soup = Cells("soup50k");
        _viewport = CentredViewport(Size);
    }

    [Benchmark]
    public int[] Project() => _viewport.Project(_soup);
}

/// <summary>What one frame costs on the wire: JSON as SignalR sends it, for a sparse and a dense view.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class FrameBenchmarks
{
    [Params(100, 500)]
    public int Size { get; set; }

    private WireFrame _frame = null!;

    [GlobalSetup]
    public void Setup()
    {
        var cells = CentredViewport(Size).Project(Cells("soup50k"));
        _frame = new WireFrame(1000, 50_000, true, 10, Size, Size, cells, false, false, 0, 300_000);
    }

    [Benchmark]
    public byte[] SerializeJson() => JsonSerializer.SerializeToUtf8Bytes(_frame, WireJson);
}

/// <summary>
/// One generation as the server experiences it with N connected clients: step, snapshot, and for
/// each client a projection through its viewport plus JSON serialisation. Network excluded.
/// </summary>
[MemoryDiagnoser]
// With an IterationSetup, BenchmarkDotNet would measure single invocations, which is far too noisy
// for a tick that allocates a large snapshot; eight invocations per iteration (eight generations
// of drift) give stable numbers.
[SimpleJob(warmupCount: 2, iterationCount: 8, invocationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class TickBenchmarks
{
    [Params("acorn@5000", "soup50k")]
    public string World { get; set; } = "";

    [Params(1, 4, 16)]
    public int Clients { get; set; }

    private Cell[] _cells = [];
    private readonly Universe _universe = new();
    private Viewport[] _viewports = [];

    [GlobalSetup]
    public void Setup()
    {
        _cells = Cells(World);
        // Clients look at slightly different places, as real ones do.
        _viewports = Enumerable.Range(0, Clients).Select(i => CentredViewport(100).Pan(i * 7, i * 3)).ToArray();
    }

    [IterationSetup]
    public void Reload() => _universe.Load(_cells);

    [Benchmark]
    public int Tick()
    {
        _universe.Step();
        var snapshot = new UniverseSnapshot(_universe.Generation, _universe.Snapshot(), true, 10, Cell.Centre);
        var bytes = 0;
        foreach (var viewport in _viewports)
        {
            var frame = new WireFrame(snapshot.Generation, snapshot.Population, snapshot.Running, snapshot.GenerationsPerSecond,
                viewport.Width, viewport.Height, viewport.Project(snapshot.Cells), false, false, 0, 300_000);
            bytes += JsonSerializer.SerializeToUtf8Bytes(frame, WireJson).Length;
        }
        return bytes;
    }
}
