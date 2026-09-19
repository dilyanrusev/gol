# Game of Life (multiplayer)

Conway's Game of Life on a 2^64 x 2^64 torus. One server runs the simulation; any number of
browsers connect over SignalR and each watches its own viewport.

## Run

```
cd src/GameOfLife.Web
dotnet run           # http://localhost:5077
```

Node.js must be on the `PATH`. `dotnet build` (and therefore `run`, `watch`, `publish`) also
builds the client: it restores the TypeScript client generator from `.config/dotnet-tools.json`,
regenerates `Scripts/generated/` from the hub interfaces, runs `npm ci` when the lock file changed,
type-checks with `tsc` and bundles with esbuild into `wwwroot/dist` (minified for Release). Each
step is incremental. The bundles then go through the SDK's static web assets pipeline: fingerprinted
file names, pre-compressed (gzip at build, gzip and Brotli at publish) and listed in the endpoint
manifest that `MapStaticAssets` serves with content negotiation and immutable caching, so a cold
page load is about 75 KB instead of 430 KB and costs the server no compression CPU. While editing
only the client, `npm run watch` rebuilds the bundles on change (without type checking; run
`npm run typecheck` or a `dotnet build` for that); in Development the files are served straight
from `wwwroot`, so the manifest does not have to know about them. Pass `-p:SkipClientBuild=true`
to build the server alone with a prebuilt `wwwroot/dist` (for example in a container without Node).

On first start the server seeds itself with `patterns/gosper_glider_gun.rle`; afterwards it
restores the universe it saved last time (see [Configuration](#configuration)). Press **Start** in
the browser to run it. Once nobody has been connected for a minute the simulation pauses itself.

While paused, one browser at a time can press **Edit cells** and flip cells by clicking them in
its view. Everyone sees a sticky banner with a countdown: the session ends on Done (which
resumes the simulation), Cancel, disconnect, or after `GameOfLife:EditTimeoutSeconds` (default
300) without an edit; the last three leave it paused. Until then Start, Step, Reset and seed
loading are refused for all clients. Edits made at generation 0 become the new seed, so Reset
returns to them.

Tests: `dotnet test`. The Web tests start the real server on Kestrel at a random port and drive it
with the .NET SignalR client and with Playwright. They use the Chromium build bundled with the
Playwright package, which the test fixture downloads on first run (about 150 MB, cached per user).

## Configuration

Everything lives in the `GameOfLife` section (`appsettings*.json`, or environment variables such
as `GameOfLife__MaxGenerationsPerSecond`). The defaults suit a developer machine;
`appsettings.Production.json` lowers the three limits for a small shared host such as a free-tier
App Service, where CPU time and outbound bandwidth are metered per day.

| Setting | Default | Production | What |
| --- | --- | --- | --- |
| `SeedFile` | `patterns/gosper_glider_gun.rle` | | Loaded when there is no saved universe. Relative to the content root. |
| `StateDirectory` | `dilyanrusev/game-of-life` under the local application data folder (`~/.local/share` on Linux, `%LOCALAPPDATA%` on Windows) | | Holds `universe.rle` and the data-protection key ring (`keys/`), so the universe, anti-forgery tokens and TempData survive a restart. Created on demand; if it cannot be written the app runs without persistence and says so in the log. |
| `SaveIntervalSeconds` | 60 | | How often the universe is saved while it changes; it is always saved at shutdown. The saved population reloads in place as the seed at generation 0. 0 disables the periodic save. |
| `PauseWhenUnwatchedSeconds` | 60 | | Grace period after the last client disconnects before the simulation pauses itself, so a closed tab does not keep the server stepping for nobody while a page reload does not interrupt it. 0 never pauses. |
| `EditTimeoutSeconds` | 300 | | Idle time after which an edit session ends on its own. |
| `MaxGenerationsPerSecond` | 60 | 20 | Ceiling of the speed slider (1–60). The CPU knob: stepping is O(live cells) per generation. |
| `MaxFramesPerSecond` | 0 (every generation) | 10 | Most frames per second sent to clients while the simulation runs faster; generations in between are computed but not sent. The bandwidth knob: a frame is ~2 bytes per visible cell when sparse, a W×H/8-byte bitmap when dense. Commands (start, pause, step, load, edits) are always sent at once. |
| `MaxViewportSize` | 500 | 150 | Largest grid a client may ask for, per side (5–500). The frame size grows with its square. |
| `MaxPopulation` | 1 048 576 | 100 000 | Most live cells an uploaded or hand-written pattern may have. Run lengths let a few bytes of RLE describe billions of cells, so the 4 MB upload limit alone bounds nothing. The seed file and the saved universe are exempt. |

## Benchmarks

```
dotnet run -c Release --project benchmarks/GameOfLife.Benchmarks -- --filter '*'
```

BenchmarkDotNet with the memory diagnoser: one generation and one snapshot copy (flat and grouped
by chunk) on three worlds (the glider gun at generation 1000, the acorn at 5000, a 50 000-cell
soup), viewport projection at 100 and 500 cells across (fresh array, a client's reused buffer, and
through the chunk index), a frame's JSON cost (index list through reflection, through source
generation, and the packed string), the cell codec, and a whole server tick with 1, 4 and 16
clients.
Committed baselines live in `benchmarks/results/`; compare a change against the latest one.
Allocation counts are exact and portable, timings are not, so the Core tests also carry allocation
budgets that fail the build when a hot path starts allocating more.

## Layout

| Path | What |
| --- | --- |
| `src/GameOfLife.Core` | Engine (`Universe`), RLE parser/writer, `Viewport`, `SimulationLoop`. No ASP.NET dependency. |
| `src/GameOfLife.Web` | Razor Pages shell, SignalR hub, hosted service, React client in `Scripts/` (`viewer/` and `editor/` are the two page bundles, `site/` the shared Bootstrap shell; `Scripts/generated/` is produced by the build from `ILifeHub`, `ILifeClient` and `Frame`; commit it, never edit it). All client dependencies, Bootstrap and SignalR included, come from `package.json`. |
| `benchmarks/GameOfLife.Benchmarks` | BenchmarkDotNet: engine step and snapshot, viewport projection (flat and indexed), frame JSON, whole server tick. Baselines in `benchmarks/results/`. |
| `tests/GameOfLife.Core.Tests` | xUnit: RLE round trips, engine vs. known patterns, viewport seam handling, loop commands. |
| `tests/GameOfLife.Web.Tests` | xUnit integration tests: hub contract over the .NET SignalR client; page behaviour in headless Chromium via Playwright (initial state on connect, controls shared across browsers, viewports per browser, exclusive editing with its banner and countdown). |
| `patterns/` | Example `.rle` files (Gosper glider gun). |

## Design

- **Sparse universe.** Live cells live in a `HashSet<Cell>` with `ulong` coordinates. A generation
  costs O(live cells). Unchecked `ulong` arithmetic gives torus wrapping for free.
- **Single writer.** `SimulationLoop` owns the `Universe`. Every mutation (start, pause, step,
  reset, load, speed, edit) is a command posted to a channel and applied between generations. After each
  change it publishes an immutable `UniverseSnapshot`. Under `MaxFramesPerSecond` the snapshot
  is built and published on the frame schedule while running (commands still publish at once, and
  a paused loop never holds a generation back), so the engine's pace and the clients' bandwidth are
  two independent settings. The hosted service restores the saved
  universe or the seed file at start, saves the latest snapshot at the configured interval and at
  shutdown (`UniverseStore`: written beside the file and swapped in, so a crash cannot leave a
  truncated one), and `ClientViewports` pauses the loop once nobody has been connected for the
  grace period.
- **Only the canvas and one toolbar under normal conditions.** The strip above the canvas has a
  leading group of in-place controls (run/pause, step, reset, speed, then Edit cells) and a trailing
  group of everything that leaves the page or opens a panel (Load pattern…, Save .rle, then a
  theme switch cycling light, dark and follow-the-system, remembered per browser, and Help). The
  readouts (generation, population, view size) are a head-up display at the top centre of the
  canvas that lets pointer events through; the connection badge shows there only while the
  connection is unhealthy, and the run state is a hidden live region for screen readers. "Pattern" is what goes in; "initial state" is what Reset returns to. Pan and
  recentre are a cross-shaped pad floating bottom-left over the canvas and zoom a stepper floating
  bottom-right (zoom out, an editable "cells across" number, zoom in), split the way phone games and
  map apps split continuous and discrete controls. Zoom is one variable, cells across the view; the
  height follows the canvas's shape so the grid fills it, and follows again when the window changes; they are the single-pointer alternative to dragging and pinching
  (WCAG 2.5.1 / 2.5.7), translucent until hovered, and grow to 44 px targets on touch. "Load
  pattern…" opens a side panel on request; it and Edit cells are disabled with a tooltip while the
  shared state forbids them, and an open panel disables its form rather than closing when someone
  else starts.
- **Razor is the shell, React is the page.** Each Razor page renders the layout, the server-side
  constants, the anti-forgery token and the URLs as data attributes on a mount element, and a
  React bundle takes over from there. Forms still post to the Razor handlers. The `useLifeHub`
  hook owns the SignalR connection and the generated typed proxy; canvas drawing stays imperative
  inside an effect. Element ids are stable because the Playwright tests drive the page by them.
- **Exclusive editing lives in the loop.** An `EditSession` names one owner (a connection id) and a
  deadline. Start, step, reset and load throw `EditInProgressException` while it exists, which is
  why the seed upload page cannot bypass it either. The hub maps the owner's viewport-relative
  clicks to absolute cells; the loop ends the session when it expires and publishes that too.
- **Per-client viewports, kept on the server.** A client never sees an absolute coordinate. It
  starts centred on the seed pattern and only sends relative changes (`Pan(dx, dy)`, `Resize`,
  `Recentre`). Deltas outside JavaScript's safe-integer range are rejected; grid size is clamped to
  5–500 cells per side. Each tick the server sends every client only the cells inside its viewport,
  packed by `CellsCodec` into a short string: delta-coded indices for sparse views, a bitmap for
  dense ones, chosen per frame. Each connection keeps its projection and encoding buffers, so a
  broadcast allocates only the string; frames returned from hub methods allocate their own.
- **Snapshots are grouped by chunk.** The loop copies the population out once per generation into
  a `SpatialIndex`: the cell array ordered by 64 x 64 chunk, with a table from chunk to range. It
  allocates the same one array as the flat copy plus the chunk table, takes about five times as
  long to build (one dictionary lookup per cell), and is built once no matter how many clients are
  connected. A client's projection then visits only the chunks its viewport overlaps (four to nine
  for a 100 x 100 view; 81 for the largest), so its cost follows what it looks at rather than the
  whole population: a 100 x 100 view of a 50 000-cell soup projects in 31 μs instead of 172. Chunk
  keys are the coordinates shifted right by six, masked on the way round the torus, so a view
  across the seam works like any other. The rebuild pays for itself from about four clients on a
  large world; storing the population in chunks inside the engine would remove it.
- **RLE everywhere.** Uploads, the pattern editor and the "save" download all go through the same
  parser/writer. Saved files carry a `#C origin x y` comment so they reload in place; files without
  it are centred on the universe.
- **Pattern editor** is a 100 x 100 matrix of real `<button>` elements: click or drag to paint,
  arrow keys to move, Space/Enter to toggle. An RLE textarea stays in sync with the grid.

Known limitation: saving a population that straddles the torus seam is refused, because its
bounding box would be the whole universe.
