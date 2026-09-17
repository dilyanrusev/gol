# Spatial index, 2026-09-17

Same machine as [the baseline](2026-09-17-baseline.md) and [the first pass](2026-09-17-optimisations.md).
One change: the loop's snapshot is a `SpatialIndex`, the population copied out grouped by 64 × 64
chunk with a table from chunk to range, and a client's projection visits only the chunks its
viewport overlaps instead of walking every cell.

## A measurement fix first

The benchmarks that mutate their world (`Step`, the whole tick) reload it in an `[IterationSetup]`,
and BenchmarkDotNet then runs each iteration as one or eight invocations: about ten to eighty calls
in total. Tiered compilation optimises a method after thirty calls and a delay, so the engine's own
code was being timed at tier 0 while the framework's precompiled code (`ToArray`, the sort, JSON)
ran optimised. It showed up as the chunked snapshot looking ten times slower than the flat copy
inside `UniverseBenchmarks` and seven times slower once given its own class. Two changes to the
harness, both kept:

- Snapshots do not mutate, so they moved to `SnapshotBenchmarks` with no per-iteration reload;
  BenchmarkDotNet chooses its own invocation count and the JIT settles.
- `UniverseBenchmarks` and `TickBenchmarks` run with `DOTNET_TieredCompilation=0`, everything
  fully optimised from the first call, which is what a long-running server reaches anyway (minus
  dynamic PGO). The tick also gained `TickFlat`, the previous pass's path, as its baseline, so the
  comparison is made under identical conditions in one run.

Earlier files' `Step` and tick rows for the small worlds were tier-0 numbers; the gun's step is
22 μs optimised against the 90 μs recorded before. The soup ran long enough to get optimised
mid-run, so its rows were roughly right.

Engine, optimised (for the record):

| World      | Step        |
|------------|------------:|
| gun@1000   |    21.9 μs  |
| acorn@5000 |   142.9 μs  |
| soup50k    | 9 553.5 μs  |

## Projection per client

`ProjectIndexed` visits the four to nine chunks under a 100 × 100 view; the flat walk tests all
50 000 cells. Both write into the client's reused buffer and sort what they found.

| Method                  | Size | Mean        | Ratio | Allocated |
|-------------------------|-----:|------------:|------:|----------:|
| Project                 |  100 |   183.9 μs  |  1.00 |  23.5 KB  |
| ProjectIntoReusedBuffer |  100 |   171.6 μs  |  0.93 |        -  |
| ProjectIndexed          |  100 |    30.8 μs  |  0.17 |        -  |
| Project                 |  500 | 2 398.2 μs  |  1.00 | 723.6 KB  |
| ProjectIntoReusedBuffer |  500 | 2 236.9 μs  |  0.93 |        -  |
| ProjectIndexed          |  500 | 1 906.9 μs  |  0.80 |        -  |

A 500 × 500 view of the 500 × 500 soup contains the whole population, so the index cannot skip
anything there; what remains is the per-cell test and the sort of some 50 000 indices, which the
bitmap encoding used for such dense views does not need. Sorting only on the sparse path is the
obvious next cut for dense views.

## The snapshot

Grouping costs one dictionary lookup per cell (the first version used two: 859 μs on the soup)
plus a second, array-only pass, against a straight copy for the flat snapshot. The bookkeeping is
kept between calls, so the extra allocation is the chunk table alone.

| Method          | World      | Mean        | Ratio | Allocated |
|-----------------|------------|------------:|------:|----------:|
| Snapshot        | gun@1000   |    0.35 μs  |  1.00 |   3.35 KB |
| SnapshotIndexed | gun@1000   |    2.00 μs  |  5.71 |   3.90 KB |
| Snapshot        | acorn@5000 |    1.21 μs  |  1.00 |  12.59 KB |
| SnapshotIndexed | acorn@5000 |    7.63 μs  |  6.32 |  13.77 KB |
| Snapshot        | soup50k    |  131.0 μs   |  1.00 | 781.3 KB  |
| SnapshotIndexed | soup50k    |  674.2 μs   |  5.16 | 784.0 KB  |

## Whole tick, 100 × 100 viewports

Flat and indexed measured side by side in one run. The machine was busier than for the earlier
files (the flat soup tick is 14.2 ms here against 13.0 ms in a quieter run minutes before), so
read the ratios, not the means, against older results.

| World      | Clients | TickFlat     | TickIndexed  | Ratio | Allocated (flat → indexed) |
|------------|--------:|-------------:|-------------:|------:|---------------------------:|
| acorn@5000 |       1 |   172.7 μs   |   172.8 μs   |  1.00 |  14.1 →  15.3 KB           |
| acorn@5000 |       4 |   194.5 μs   |   205.3 μs   |  1.06 |  18.3 →  19.5 KB           |
| acorn@5000 |      16 |   235.2 μs   |   240.8 μs   |  1.02 |  24.8 →  26.0 KB           |
| soup50k    |       1 | 14 215 μs    | 14 831 μs    |  1.05 | 687.0 → 689.7 KB           |
| soup50k    |       4 | 15 031 μs    | 14 467 μs    |  0.96 | 702.6 → 705.3 KB           |
| soup50k    |      16 | 16 935 μs    | 15 884 μs    |  0.94 | 765.0 → 767.7 KB           |

## What this means

- Per client the projection no longer depends on the population: about 31 μs for a 100 × 100 view
  of the soup instead of 172 μs, and the same for any larger world, since only the chunks under
  the view are read.
- The price is paid once per generation, not per client: building the chunked snapshot costs
  about 540 μs more than the flat copy on the soup (5 to 6 times the copy on every world). The
  index pays for itself from about four clients on the soup; with one client it costs 5 %, and on
  the small worlds it is within noise either way.
- The tick is still the step's: 9.5 of the soup's 14 ms, and on the acorn most of the 170 μs.
  Projection, packing and JSON for 16 clients now add about 1.7 ms on the soup and 70 μs on the
  acorn.
- The rebuild exists only because the engine stores a flat `HashSet<Cell>` and the index has to be
  derived from it every generation. Keeping the population in chunks inside the engine (so a
  generation produces the grouped array directly, and unchanged chunks could even be shared between
  snapshots) removes the 540 μs and is the next pass, together with sorting only on the sparse path.
