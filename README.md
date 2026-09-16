# Game of Life (multiplayer)

Conway's Game of Life on a 2^64 x 2^64 torus. One server runs the simulation; any number of
browsers connect over SignalR and each watches its own viewport.

## Run

```
cd src/GameOfLife.Web
npm install          # also copies the SignalR browser bundle into wwwroot/lib/signalr
npm run build        # or: npm run watch  (rebuilds TypeScript on change)
dotnet run           # http://localhost:5077
```

The server seeds itself with `patterns/gosper_glider_gun.rle` (configurable through
`GameOfLife:SeedFile`). Press **Start** in the browser to run it.

Tests: `dotnet test`

## Layout

| Path | What |
| --- | --- |
| `src/GameOfLife.Core` | Engine (`Universe`), RLE parser/writer, `Viewport`, `SimulationLoop`. No ASP.NET dependency. |
| `src/GameOfLife.Web` | Razor Pages UI, SignalR hub, hosted service, TypeScript client in `Scripts/`. |
| `tests/GameOfLife.Core.Tests` | xUnit: RLE round trips, engine vs. known patterns, viewport seam handling, loop commands. |
| `patterns/` | Example `.rle` files (Gosper glider gun). |

## Design

- **Sparse universe.** Live cells live in a `HashSet<Cell>` with `ulong` coordinates. A generation
  costs O(live cells). Unchecked `ulong` arithmetic gives torus wrapping for free.
- **Single writer.** `SimulationLoop` owns the `Universe`. Every mutation (start, pause, step,
  reset, load, speed) is a command posted to a channel and applied between generations. After each
  change it publishes an immutable `UniverseSnapshot`.
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
