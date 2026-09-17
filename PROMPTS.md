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

### 7. Generated TypeScript client

> Is it possible to use something like OpenAPI to automate the sync for Frame and the hub methods
> between .NET and TS?

> From the github page, it seems that this can be easily integrated in the .csproj, as a build step
> that happens after Build. It could test if the dotnet tool is in path, if not, install it. Another
> step would depend on the installation step and manually invoke the tool to generate the client
> proxy. Do you see downsides?

> Can you test if the tool handls .net 10 yourself?

> Integrate it into the .csproj as we discussed

Result: TypedSignalR.Client.TypeScript (pinned in `.config/dotnet-tools.json`) generates
`Scripts/generated/` from `[Hub] ILifeHub`, `[Receiver] ILifeClient` and `[TranspilationSource]
Frame`. `GameOfLife.Web.csproj` chains four incremental targets after `Build`: `dotnet tool restore`,
the generator, `npm ci`, `npm run build`; all skipped for design-time builds and with
`-p:SkipClientBuild=true`. `LifeHub` implements `ILifeHub` (viewport methods now return
`Task<Frame>`). `viewer.ts` uses the generated proxy and receiver instead of string method names.
Verified: full build, incremental rebuild skips all four targets, publish from a tree without
`wwwroot/js` still ships the JS, scripted client exercises the hub against the built server.

### 8. Integration tests

> What do you recommend for integration tests? Puppeteer or PuppeteerSharp?

> Can playwright be configured to use an already installed chromium instance?

> Ok, for the sake of stable testing, use the embedded chromium that PW ships with

Result: recommended Playwright for .NET over either Puppeteer flavour (stays in xUnit, multiple
browser contexts for multiplayer scenarios, touch emulation, auto-waiting). New
`tests/GameOfLife.Web.Tests`: `WebAppFixture` hosts the app with `WebApplicationFactory` in its
.NET 10 Kestrel mode at a random port and launches Playwright's bundled Chromium (installed by the
fixture on first run). `HubTests` cover the hub contract with the .NET SignalR client (frame on
connect, refresh, resize clamping, rejected pan, shared start/pause, per-client viewports).
`BrowserTests` cover the page: paused/running state on connect, start in one browser seen in
another, zoom affecting one browser only, controls disabled when the connection fails.
`Program.cs` gained `public partial class Program` for the factory.

### 9. Typed hub calls in the viewer

> in viewer.ts, I see `call` that accepts method name as string. Can you make it use the generated
> proxy files, i.e. ILifeHub from Scripts/generated/TypedSignalR.Client/GameOfLife.Web.Hubs.ts?

Result: `call` is generic over `keyof ILifeHub` and takes `Parameters<ILifeHub[M]>`, looking the
method up on the generated proxy. Call sites pass the name and arguments only; an unknown name or
wrong argument list is a compile error (verified with a scratch file: 4 deliberate mistakes, 4
errors, the valid call accepted). Web tests green.

### 10. Exclusive editing of the live universe

> I have an additional requirement: when paused, the client (the browser) can edit the pattern
> inside the view. Make sure that only one client can enter inside edit mode. When in edit mode, no
> other client can start the simulation. Add tests (both unit and integration), and also make sure
> that the UI explains to the user what is going on. Before implementing, do you see problems with
> this feature request, and also do you need additional decisions/information?

> I agree with your decisions. In addition, the banner should display both time remainig until
> unlock, and a progress bar that shows how much time is remaning (starts full, goes to empty as
> time passes by). Style it with bootstrap shadows. Let it be sticky on the top. Use bootstrap
> colors: success when there is a lot of time, warning when 50% and below, danger when 20% and
> below. Ask me if I something is unclear

Decisions agreed: Done/Cancel/disconnect/5-minute idle timeout end a session; Start, Step, Reset
and seed loading are refused meanwhile (speed and viewport changes stay allowed); editing requires
a paused simulation; edits are live for everyone with Cancel restoring the pre-edit state; edits at
generation 0 become the seed. Clicks are refused below 4 px per cell.

Result: Core gains `EditSession`, `EditInProgressException`, `Universe.Toggle/Replace`, and
`SimulationLoop.{BeginEdit,ToggleCell,EndEdit,CancelEdit,ReleaseEdit}Async` with expiry handled in
the loop; commands now complete only after the snapshot is rebuilt. `Frame` carries
`Editing/EditingByMe/EditRemainingMs/EditTimeoutMs`; the hub exposes the four edit methods, maps
clicks through the caller's viewport, releases on disconnect and explains refusals. The page has an
Edit button, a sticky shadowed banner with countdown and draining progress bar (success > 50 % >
warning > 20 % > danger) shown to editor and observers alike, and a crosshair cursor while editing.
Tests: 20 Core unit tests, 9 hub tests, 5 browser tests (banners, clicking cells, cancel, the
countdown draining through the colours and unlocking, edit button waiting for a pause).

### 11. Done resumes; tooltip on the disabled Edit button

> After interacting with the server, I changed my mind. It feels better to resume once "done" is
> clicked. Also, add a bootstrap tooltip on the disabled edit cells button that explains that you
> have to pause first.

Result: `SimulationLoop.EndEditAsync(owner, resume)` commits and, when asked, starts the
simulation in the same command; the hub's `EndEdit` passes `resume: true`. Cancel, disconnect and
expiry still leave it paused. The Edit button sits in a wrapper carrying a Bootstrap tooltip
(disabled buttons get no pointer events) whose text is "pause first" while running and "another
client is editing" while locked; the tooltip instance exists only while there is something to
explain (Bootstrap re-creates a shown tooltip on every content change, which flickered at ten
frames a second while running, and can leave a hidden element behind on a quick re-hover). Tests
updated and added at all three levels; Done now runs a generation immediately, which the tests
account for by checking the adopted seed rather than the transient population.

### 12. React and esbuild

> It seems the ts code is too complicated for pure DOM manipulations. It should be ported to React,
> and now is the time to start using esbuild. This will remove the need to copy the SignalR to
> wwwroot manually. What information do you need to do this?

> I agree with your defaults, do it

Decisions: both pages ported; Razor stays the shell (layout, alerts, constants and anti-forgery
token via data attributes, form handlers); react-bootstrap for behaviour with Bootstrap itself
moved from the vendored copy to npm and bundled by esbuild; the build type-checks with `tsc` then
bundles with esbuild (minified for Release); Playwright stays the only client test layer with all
element ids preserved.

Result: `Scripts/viewer` (`useLifeHub` hook over the generated proxy, `Viewer`, `UniverseCanvas`,
`EditBanner`, `StatusBar`, `SimulationCard` with a react-bootstrap tooltip, `ViewportCard`,
`SeedCard`), `Scripts/editor` (`Editor` with memoised rows, painting, roving tabindex, inline RLE
errors instead of alert()), `Scripts/site` (Bootstrap CSS/JS + site styles), `build.mjs`, new
`tsconfig.json`, `package.json` without the copy-libs step, csproj `BuildClient` target, layout and
pages reduced to mount elements. `wwwroot/lib` and `wwwroot/css` are gone; `wwwroot/dist` is
ignored. All existing tests pass unchanged except the upload test, which now reads the anti-forgery
token from the mount element. Three new Playwright tests cover the seed editor (presets, clicks,
keyboard, RLE round trip, inline errors, seeding the universe through the Razor handler).

### 13. Viewer configuration through a React context

> Let's use a context for the ViewerConfig, so that it doesn't need to be passed around. Initial
> state is seeded from the data attributes, just like now.

Result: `Scripts/viewer/ViewerConfigContext.tsx` holds the `ViewerConfig` type, a
`ViewerConfigProvider` and a `useViewerConfig` hook that throws outside a provider. `main.tsx` still
reads the mount element's data attributes and now wraps `<Viewer />` in the provider; `Viewer`,
`SimulationCard`, `ViewportCard` and `SeedCard` read the config from the hook instead of props.

### 14. Bootstrap icons and tooltips on the viewport controls

> The viewport controls use unicode arrows, but they don't render OK in Firefox. Let's use bootstrap
> icons, and more specifically, the filled caret icons for the directions, and byllseye for
> centering. Also, add bootstrap tooltips.

Result: `react-bootstrap-icons` (SVG components, so no icon font to serve) provides the filled
carets, the bullseye and zoom icons; every viewport button is wrapped in a react-bootstrap tooltip
that also states the pan step in cells. Accessible names stay on the buttons; icons are aria-hidden.
