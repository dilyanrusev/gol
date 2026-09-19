using MessagePack;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using GameOfLife.Core;
using static GameOfLife.Benchmarks.Workloads;

namespace GameOfLife.Benchmarks;

/// <summary>
/// For benchmarks that mutate their state and so need an <c>[IterationSetup]</c>: BenchmarkDotNet
/// then runs each iteration as a handful of invocations, far fewer than the thirty calls plus a
/// delay that tiered compilation needs before it optimises a method. The engine's own code would
/// be timed at tier 0 while the framework's precompiled code ran optimised. Turning tiering off
/// compiles everything fully optimised from the first call, which is what a long-running server
/// reaches anyway (minus dynamic PGO).
/// </summary>
public class FullJitConfig : ManualConfig
{
    public FullJitConfig(int invocationCount)
    {
        AddJob(Job.Default
            .WithWarmupCount(2)
            .WithIterationCount(8)
            .WithInvocationCount(invocationCount)
            .WithUnrollFactor(1)
            .WithEnvironmentVariables(new EnvironmentVariable("DOTNET_TieredCompilation", "0")));
    }
}

public sealed class FullJitSingleInvocationConfig() : FullJitConfig(1);

public sealed class FullJitEightInvocationsConfig() : FullJitConfig(8);

/// <summary>The engine: one generation.</summary>
[MemoryDiagnoser]
[Config(typeof(FullJitSingleInvocationConfig))]
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
}

/// <summary>
/// Copying the population out once per generation: the flat array, and the array grouped by
/// chunk with its chunk table. Snapshots do not mutate, so no reload is needed and the JIT settles.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class SnapshotBenchmarks
{
    [Params("gun@1000", "acorn@5000", "soup50k")]
    public string World { get; set; } = "";

    private readonly Universe _universe = new();

    [GlobalSetup]
    public void LoadWorld() => _universe.Load(Cells(World));

    [Benchmark(Baseline = true)]
    public Cell[] Snapshot() => _universe.Snapshot();

    [Benchmark]
    public SpatialIndex SnapshotIndexed() => _universe.SnapshotIndexed();
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
    private SpatialIndex _index = SpatialIndex.Empty;
    private Viewport _viewport;
    private int[] _buffer = new int[256];

    [GlobalSetup]
    public void Setup()
    {
        _soup = Cells("soup50k");
        _index = SpatialIndex.Build(_soup);
        _viewport = CentredViewport(Size);
        _viewport.Project(_soup, ref _buffer); // grow the reused buffer once, as a client's first frame would
    }

    [Benchmark(Baseline = true)]
    public int[] Project() => _viewport.Project(_soup);

    [Benchmark]
    public int ProjectIntoReusedBuffer() => _viewport.Project(_soup, ref _buffer);

    [Benchmark]
    public int ProjectIndexed() => _index.Project(_viewport, ref _buffer);
}

/// <summary>
/// What one frame costs on the wire for a sparse and a dense view: the original index list through
/// reflection JSON, the same through source generation, the packed cells through JSON (base64) and
/// through MessagePack (bytes as they are, the protocol the browser uses).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class FrameBenchmarks
{
    [Params(100, 500)]
    public int Size { get; set; }

    private IndexFrame _indexFrame = null!;
    private WireFrame _packedFrame = null!;

    [GlobalSetup]
    public void Setup()
    {
        var cells = CentredViewport(Size).Project(Cells("soup50k"));
        _indexFrame = new IndexFrame(1000, 50_000, true, 10, Size, Size, cells, false, false, 0, 300_000);
        _packedFrame = new WireFrame(1000, 50_000, true, 10, Size, Size, CellsCodec.Encode(cells, Size, Size), false, false, 0, 300_000);
    }

    [Benchmark(Baseline = true)]
    public byte[] SerializeJson() => JsonSerializer.SerializeToUtf8Bytes(_indexFrame, WireJson);

    [Benchmark]
    public byte[] SerializeJsonSourceGen() => JsonSerializer.SerializeToUtf8Bytes(_indexFrame, BenchJsonContext.Default.IndexFrame);

    [Benchmark]
    public byte[] SerializeJsonPacked() => JsonSerializer.SerializeToUtf8Bytes(_packedFrame, BenchJsonContext.Default.WireFrame);

    [Benchmark]
    public byte[] SerializeMessagePackPacked() => MessagePackSerializer.Serialize(_packedFrame);
}

/// <summary>Packing the cells: the encoder with a kept scratch buffer, so only the result is allocated.</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
[MarkdownExporterAttribute.GitHub]
public class CodecBenchmarks
{
    [Params("acorn@5000/100", "soup50k/100", "soup50k/500")]
    public string Case { get; set; } = "";

    private int[] _indices = [];
    private int _size;
    private byte[] _scratch = [];

    [GlobalSetup]
    public void Setup()
    {
        var parts = Case.Split('/');
        _size = int.Parse(parts[1]);
        _indices = CentredViewport(_size).Project(Cells(parts[0]));
        CellsCodec.Encode(_indices, _size, _size, ref _scratch);
    }

    [Benchmark]
    public byte[] Encode() => CellsCodec.Encode(_indices, _size, _size, ref _scratch);
}

/// <summary>
/// One generation as the server experiences it with N connected clients: step, snapshot, and for
/// each client a projection through its viewport plus MessagePack serialisation. Network excluded.
/// <c>TickFlat</c> is the previous pass (flat snapshot, every client walks the population);
/// <c>TickIndexed</c> is the current one (chunked snapshot, each client visits its chunks).
/// </summary>
[MemoryDiagnoser]
// With an IterationSetup, BenchmarkDotNet would measure single invocations, which is far too noisy
// for a tick that allocates a large snapshot; eight invocations per iteration (eight generations
// of drift) give stable numbers.
[Config(typeof(FullJitEightInvocationsConfig))]
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
    private int[][] _buffers = [];
    private byte[][] _scratch = [];

    [GlobalSetup]
    public void Setup()
    {
        _cells = Cells(World);
        // Clients look at slightly different places, as real ones do, and each keeps its projection buffer.
        _viewports = Enumerable.Range(0, Clients).Select(i => CentredViewport(100).Pan(i * 7, i * 3)).ToArray();
        _buffers = _viewports.Select(_ => new int[256]).ToArray();
        _scratch = _viewports.Select(_ => Array.Empty<byte>()).ToArray();
    }

    [IterationSetup]
    public void Reload() => _universe.Load(_cells);

    [Benchmark(Baseline = true)]
    public int TickFlat()
    {
        _universe.Step();
        var cells = _universe.Snapshot();
        var bytes = 0;
        for (var i = 0; i < _viewports.Length; i++)
        {
            var viewport = _viewports[i];
            var count = viewport.Project(cells, ref _buffers[i]);
            bytes += Send(i, count, _universe.Generation, cells.Length);
        }
        return bytes;
    }

    [Benchmark]
    public int TickIndexed()
    {
        _universe.Step();
        var snapshot = new UniverseSnapshot(_universe.Generation, _universe.SnapshotIndexed(), true, 10, Cell.Centre);
        var bytes = 0;
        for (var i = 0; i < _viewports.Length; i++)
        {
            var viewport = _viewports[i];
            var count = snapshot.Index.Project(viewport, ref _buffers[i]);
            bytes += Send(i, count, snapshot.Generation, snapshot.Population);
        }
        return bytes;
    }

    /// <summary>Packs client <paramref name="i"/>'s projected cells into a frame and serialises it.</summary>
    private int Send(int i, int count, ulong generation, int population)
    {
        var viewport = _viewports[i];
        var cells = CellsCodec.Encode(_buffers[i].AsSpan(0, count), viewport.Width, viewport.Height, ref _scratch[i]);
        var frame = new WireFrame(generation, population, true, 10, viewport.Width, viewport.Height, cells, false, false, 0, 300_000);
        return MessagePackSerializer.Serialize(frame).Length;
    }
}
