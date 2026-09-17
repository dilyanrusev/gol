# After the first optimisation pass, 2026-09-17

Same machine and configuration as [the baseline](2026-09-17-baseline.md). Three changes, measured
one at a time: JSON source generation, a per-connection projection buffer, and packed cells on the
wire. Engine numbers (step, snapshot) are unchanged and not repeated.

## 1. JSON source generation

The frame's eleven scalar properties go through compiled metadata; the array of indices is written
the same way as before. Expected to be small, and it was.

| Method                 | Size | Mean      | Ratio | Allocated | Alloc ratio |
|------------------------|-----:|----------:|------:|----------:|------------:|
| SerializeJson          |  100 |  10.3 μs  |  1.00 |  10.08 KB |        1.00 |
| SerializeJsonSourceGen |  100 |   9.8 μs  |  0.96 |   9.77 KB |        0.97 |
| SerializeJson          |  500 | 290.0 μs  |  1.00 | 320.97 KB |        1.00 |
| SerializeJsonSourceGen |  500 | 277.5 μs  |  0.96 | 320.42 KB |        1.00 |

Kept for its startup and trimming benefits. Lesson learnt: inserting a context into the serializer
options' resolver chain drops the implicit reflection resolver; `long` hub arguments stopped
binding until it was added back explicitly. A test now checks both.

## 2. Per-connection projection buffer

`Viewport.Project` gained a span-based overload writing into a caller-owned, growable buffer; the
broadcast keeps one per connection. The enumerable overload allocated an enumerator even when
given an array, which the allocation-budget test caught.

| Method                  | Size | Mean       | Ratio | Allocated | Alloc ratio |
|-------------------------|-----:|-----------:|------:|----------:|------------:|
| Project                 |  100 |   174.1 μs |  1.00 |  23.5 KB  |        1.00 |
| ProjectIntoReusedBuffer |  100 |   169.0 μs |  0.97 |        -  |        0.00 |
| Project                 |  500 | 2 347.4 μs |  1.00 | 723.6 KB  |        1.00 |
| ProjectIntoReusedBuffer |  500 | 2 296.6 μs |  0.98 |        -  |        0.00 |

Whole tick, 100 × 100 viewports (baseline → after step 2):

| World      | Clients | Mean                 | Allocated             |
|------------|--------:|---------------------:|----------------------:|
| acorn@5000 |       1 |  193.7 →  185.7 μs   |   20.4 →  14.5 KB     |
| acorn@5000 |       4 |  329.6 →  238.2 μs   |   40.1 →  19.5 KB     |
| acorn@5000 |      16 |  749.7 →  446.3 μs   |   62.1 →  26.8 KB     |
| soup50k    |       1 | 12 946 → 12 817 μs   |  716.1 → 690.6 KB     |
| soup50k    |      16 | 15 783 → 15 031 μs   | 1 213.2 → 824.4 KB    |

The 14.5 KB that remain on the one-client acorn tick are the snapshot copy (12.9 KB) and the JSON.

## 3. Packed cells (`CellsCodec`)

Cells travel as a tagged base64 string: delta-coded indices when sparse, a bitmap when at least
one cell in twelve is alive. The encoder writes into a per-connection scratch buffer; only the
string is allocated. The client decodes to indices.

Frame on the wire, soup:

| Method                 | Size | Mean       | Ratio | Allocated | Alloc ratio |
|------------------------|-----:|-----------:|------:|----------:|------------:|
| SerializeJson          |  100 |  14.4 μs   |  1.00 |  10.11 KB |        1.00 |
| SerializeJsonSourceGen |  100 |  11.3 μs   |  0.78 |   9.80 KB |        0.97 |
| SerializeJsonPacked    |  100 |   0.33 μs  |  0.02 |   1.84 KB |        0.18 |
| SerializeJson          |  500 | 353.3 μs   |  1.00 | 320.99 KB |        1.00 |
| SerializeJsonSourceGen |  500 | 301.0 μs   |  0.85 | 320.69 KB |        1.00 |
| SerializeJsonPacked    |  500 |  40.3 μs   |  0.11 |  40.93 KB |        0.13 |

(The reflection row is slower here than in the baseline run; the machine was busier. Ratios are
the comparable figures.)

Encoding itself, with a warm scratch buffer (the allocation is the returned string):

| Case           | Mean      | Allocated |
|----------------|----------:|----------:|
| acorn@5000/100 |   0.36 μs |   1.02 KB |
| soup50k/100    |   1.66 μs |   3.28 KB |
| soup50k/500    |  39.5 μs  |  81.41 KB |

Whole tick, 100 × 100 viewports (baseline → after step 3):

| World      | Clients | Mean                 | Allocated             |
|------------|--------:|---------------------:|----------------------:|
| acorn@5000 |       1 |  193.7 →  144.4 μs   |   20.4 →  14.2 KB     |
| acorn@5000 |       4 |  329.6 →  207.8 μs   |   40.1 →  18.4 KB     |
| acorn@5000 |      16 |  749.7 →  327.0 μs   |   62.1 →  24.9 KB     |
| soup50k    |       1 | 12 946 → 11 927 μs   |  716.1 → 687.0 KB     |
| soup50k    |       4 | 13 374 → 12 533 μs   |  817.2 → 702.6 KB     |
| soup50k    |      16 | 15 783 → 13 949 μs   | 1 213.2 → 765.0 KB    |

## Where this leaves the server

- Per client per generation on a small world: about 12 μs and 0.7 KB, down from 35 μs and 3 KB,
  with a frame of about 1 KB on the wire instead of 10 KB.
- A dense 500 × 500 view costs 41 KB on the wire instead of 321 KB.
- The remaining per-tick cost on the acorn is the snapshot copy; on the soup it is the step
  itself (12 ms of the 12.5). Those are the engine's, and the next targets: a spatial index so
  projection touches only the viewport's region instead of the whole population, and a cheaper
  generation on large populations.
