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
and compiles the TypeScript into `wwwroot/js`. Each step is incremental. While editing only
TypeScript, `npm run watch` is still the fastest loop. Pass `-p:SkipClientBuild=true` to build
the server alone (for example in a container without Node).

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

## Layout

| Path | What |
| --- | --- |
| `src/GameOfLife.Core` | Engine (`Universe`), RLE parser/writer, `Viewport`, `SimulationLoop`. No ASP.NET dependency. |
| `src/GameOfLife.Web` | Razor Pages UI, SignalR hub, hosted service, TypeScript client in `Scripts/` (`Scripts/generated/` is produced by the build from `ILifeHub`, `ILifeClient` and `Frame`; commit it, never edit it). |
| `tests/GameOfLife.Core.Tests` | xUnit: RLE round trips, engine vs. known patterns, viewport seam handling, loop commands. |
| `tests/GameOfLife.Web.Tests` | xUnit integration tests: hub contract over the .NET SignalR client; page behaviour in headless Chromium via Playwright (initial state on connect, controls shared across browsers, viewports per browser, exclusive editing with its banner and countdown). |
| `patterns/` | Example `.rle` files (Gosper glider gun). |

## Design

- **Sparse universe.** Live cells live in a `HashSet<Cell>` with `ulong` coordinates. A generation
  costs O(live cells). Unchecked `ulong` arithmetic gives torus wrapping for free.
- **Single writer.** `SimulationLoop` owns the `Universe`. Every mutation (start, pause, step,
  reset, load, speed, edit) is a command posted to a channel and applied between generations. After each
  change it publishes an immutable `UniverseSnapshot`.
- **Exclusive editing lives in the loop.** An `EditSession` names one owner (a connection id) and a
  deadline. Start, step, reset and load throw `EditInProgressException` while it exists, which is
  why the seed upload page cannot bypass it either. The hub maps the owner's viewport-relative
  clicks to absolute cells; the loop ends the session when it expires and publishes that too.
- **Per-client viewports, kept on the server.** A client never sees an absolute coordinate. It
  starts centred on the seed pattern and only sends relative changes (`Pan(dx, dy)`, `Resize`,
  `Recentre`). Deltas outside JavaScript's safe-integer range are rejected; grid size is clamped to
  5–500 cells per side. Each tick the server sends every client only the cells inside its viewport,
  packed as `y * width + x` indices.
- **RLE everywhere.** Uploads, the seed editor and the "save" download all go through the same
  parser/writer. Saved files carry a `#C origin x y` comment so they reload in place; files without
  it are centred on the universe.
- **Seed editor** is a 100 x 100 matrix of real `<button>` elements: click or drag to paint,
  arrow keys to move, Space/Enter to toggle. An RLE textarea stays in sync with the grid.

Known limitation: saving a population that straddles the torus seam is refused, because its
bounding box would be the whole universe.
