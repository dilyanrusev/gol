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
step is incremental. While editing only the client, `npm run watch` rebuilds the bundles on change
(without type checking; run `npm run typecheck` or a `dotnet build` for that). Pass
`-p:SkipClientBuild=true` to build the server alone (for example in a container without Node).

The server seeds itself with `patterns/gosper_glider_gun.rle` (configurable through
`GameOfLife:SeedFile`). Press **Start** in the browser to run it.

While paused, one browser at a time can press **Edit cells** and flip cells by clicking them in
its view. Everyone sees a sticky banner with a countdown: the session ends on Done (which
resumes the simulation), Cancel, disconnect, or after `GameOfLife:EditTimeoutSeconds` (default
300) without an edit; the last three leave it paused. Until then Start, Step, Reset and seed
loading are refused for all clients. Edits made at generation 0 become the new seed, so Reset
returns to them.

Tests: `dotnet test`. The Web tests start the real server on Kestrel at a random port and drive it
with the .NET SignalR client and with Playwright. They use the Chromium build bundled with the
Playwright package, which the test fixture downloads on first run (about 150 MB, cached per user).

## Benchmarks

```
dotnet run -c Release --project benchmarks/GameOfLife.Benchmarks -- --filter '*'
```

BenchmarkDotNet with the memory diagnoser: one generation and one snapshot copy on three worlds
(the glider gun at generation 1000, the acorn at 5000, a 50 000-cell soup), viewport projection at
100 and 500 cells across, a frame's JSON cost, and a whole server tick with 1, 4 and 16 clients.
Committed baselines live in `benchmarks/results/`; compare a change against the latest one.
Allocation counts are exact and portable, timings are not, so the Core tests also carry allocation
budgets that fail the build when a hot path starts allocating more.

## Layout

| Path | What |
| --- | --- |
| `src/GameOfLife.Core` | Engine (`Universe`), RLE parser/writer, `Viewport`, `SimulationLoop`. No ASP.NET dependency. |
| `src/GameOfLife.Web` | Razor Pages shell, SignalR hub, hosted service, React client in `Scripts/` (`viewer/` and `editor/` are the two page bundles, `site/` the shared Bootstrap shell; `Scripts/generated/` is produced by the build from `ILifeHub`, `ILifeClient` and `Frame`; commit it, never edit it). All client dependencies, Bootstrap and SignalR included, come from `package.json`. |
| `benchmarks/GameOfLife.Benchmarks` | BenchmarkDotNet: engine step and snapshot, viewport projection, frame JSON, whole server tick. Baselines in `benchmarks/results/`. |
| `tests/GameOfLife.Core.Tests` | xUnit: RLE round trips, engine vs. known patterns, viewport seam handling, loop commands. |
| `tests/GameOfLife.Web.Tests` | xUnit integration tests: hub contract over the .NET SignalR client; page behaviour in headless Chromium via Playwright (initial state on connect, controls shared across browsers, viewports per browser, exclusive editing with its banner and countdown). |
| `patterns/` | Example `.rle` files (Gosper glider gun). |

## Design

- **Sparse universe.** Live cells live in a `HashSet<Cell>` with `ulong` coordinates. A generation
  costs O(live cells). Unchecked `ulong` arithmetic gives torus wrapping for free.
- **Single writer.** `SimulationLoop` owns the `Universe`. Every mutation (start, pause, step,
  reset, load, speed, edit) is a command posted to a channel and applied between generations. After each
  change it publishes an immutable `UniverseSnapshot`.
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
  packed as `y * width + x` indices.
- **RLE everywhere.** Uploads, the pattern editor and the "save" download all go through the same
  parser/writer. Saved files carry a `#C origin x y` comment so they reload in place; files without
  it are centred on the universe.
- **Pattern editor** is a 100 x 100 matrix of real `<button>` elements: click or drag to paint,
  arrow keys to move, Space/Enter to toggle. An RLE textarea stays in sync with the grid.

Known limitation: saving a population that straddles the torus seam is refused, because its
bounding box would be the whole universe.
