# Prompt history

A record of the prompts given to Claude Code (model: Claude Fable 5.1) while building this project,
in order, with a short note of what each one produced. Project-level instructions live in
`CLAUDE.md`; the assignment is in `TASK.md`.

## Session 1 — 2026-09-16

### 1. Orientation

> Can you take a look at @CLAUDE.md and @TASK.md

Result: an assessment of the task against the chosen stack (ASP.NET Core, SignalR, TypeScript +
Canvas, Razor Pages, Bootstrap), a proposal for the core design (sparse `HashSet` of live cells with
`ulong` coordinates, single simulation instance with a hub broadcasting to observers, RLE for
save/load, per-client viewport) and three open questions: start on button vs. autostart, per-client
vs. shared viewport, and tsc vs. a bundler.

### 2. Decisions

> Use a start button to start the simulation. Each client has unique viewport. Let's start with tsc,
> and if we need custom CSS that goes outside the scope of CSS and we need SCSS or LESS, we'll
> switch to esbuild. I'd like to have a npm run watch that would rebuild the TS. I also want to have
> tests for the server functionality. I guess the serialization would be a good place to start.
> Sparse HashMap is the logical way to go, and wrapping should be free in .NET, as it is the defined
> behaviour for unsigned ints. Can you think of anything else before the scaffolding begins?

Result: a list of remaining design points — 64-bit coordinates do not survive the trip to
JavaScript, concurrency between request threads and the simulation loop (single-writer command
channel), viewport clipping across the torus seam, how to load SignalR without a bundler, RLE
details (origin comment, seam-straddling saves, B3/S23 only, download instead of server-side save),
controls (Start/Pause/Step/Reset/speed), viewport size clamp, project layout, and the test list.

### 3. Wire format and testing requirements

> Ok, if the client has 64bit floating points, how would it render its viewport? It can't even work
> with them. Therefore, we assume origin is the center of the original seeding pattern at start.
> Then, the position is stored only on the server. The client gives only relative changes when the
> origin changes. Each step of the simulation only returnes the viewport (as you said, clamped. Let
> it be a constant that we can later build UI for. Let's say 500 max.). The client doesn't need to
> know its actual coordinates, therefore no confusing string-as-number on the JS side. We just
> hard-define that you can't make relative changes to the viewport origin that are impossible to
> represent as 64-bit float, and we would flat out reject them. When it comes to synchronization,
> I agree. Only the simulation can do edits. Also, I want the simulation in the test to be compared
> to known states of known patterns, so that I can be sure that it is correct.

Sent while that turn was running:

> Also, initial seed pattern could be a matrix of DOM buttons - easy editing with clicking and focus
> chaning with the keyboard.

> this is about the UI that edits the initial seed pattern

Result: the full scaffold.

- `src/GameOfLife.Core`: `Cell`, `Universe`, `Pattern`, `Viewport`, `SimulationLoop`,
  `UniverseSnapshot`, `Rle/RleParser`, `Rle/RleWriter`.
- `tests/GameOfLife.Core.Tests`: 69 xUnit tests — RLE parsing/writing/round trips, engine
  behaviour compared to documented facts about known patterns (oscillator periods, glider and LWSS
  translation, glider crossing the seam, diehard dying at generation 130, R-pentomino at 1103/116,
  acorn at 5206/633, Gosper gun emitting a glider every 30 generations), viewport seam projection
  and delta rejection, simulation loop commands.
- `src/GameOfLife.Web`: `LifeHub`, `ClientViewports` (server-side per-connection viewports,
  relative pan/resize/recentre, frames of packed cell indices), `SimulationHostedService`,
  Razor Pages `Index` (canvas viewer, controls, upload, export) and `Editor` (100 x 100 matrix of
  `<button>`s with keyboard navigation and a synced RLE textarea), TypeScript sources in `Scripts/`
  built with `tsc` (`npm run build` / `npm run watch`).
- `patterns/gosper_glider_gun.rle`, `README.md`, `.gitignore` entries for client build output.

Verification done in the same turn: all tests green; a Node SignalR client exercised
connect/pan/resize/rejected pan/step/start/pause/reset against the running server; HTTP checks of
pages, static files, upload and export; a Chrome session confirmed rendering, animation, the editor
and seeding from it.

### 4. This file

> Can you save to prompt history in PROMPTS.md?

Result: this file.

## Session 2 — 2026-09-17

### 5. Initialise the client from the hub's state

> on the client side, when a new browser connects, the state of the world is not taken into
> account. E.g. whether the simulation is running, paused, etc, is not used. In the client, on
> connect to the hub, the state should be initialized according to the Frame

Result: `viewer.ts` now calls `Refresh` right after `connection.start()` resolves (the same path
as after a reconnect) and applies the returned frame, instead of relying on the hub's push from
`OnConnectedAsync`. The frame drives a new running/paused badge, disables Start while running and
Pause while paused, and the simulation buttons and speed slider stay disabled until the first
frame arrives (and again while reconnecting or after the connection closes).

### 6. Typed hub clients

> Can you use Hub<T>, so that clients are more type-safe and not just string constants?

Result: new `Hubs/ILifeClient.cs` declares `ReceiveFrame(Frame, CancellationToken)`; `LifeHub`
derives from `Hub<ILifeClient>` and `ClientViewports` takes `IHubContext<LifeHub, ILifeClient>`,
so every push is a compiled method call and the `FrameMethod` string constant is gone. The browser
subscribes to `ReceiveFrame` (the interface method name) in `viewer.ts`.
