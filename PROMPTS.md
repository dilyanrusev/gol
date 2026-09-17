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

### 15. Viewport toolbar instead of a card

> Let's save some space. Lets turn the viewport card into a vertical toolbar that is on top until
> the breakpoint (I think md currently) and top and bottom after the breakpoint. It should be thin
> and take all width of the universe canvas. THe primary UI for changing the viewport is
> tap/pan/wheel, etc

Assumption stated: thin horizontal strips spanning the canvas width; below the layout's actual
breakpoint (lg) one wrapping strip above the canvas holds everything, from lg up navigation and
zoom stay above and the grid-size controls move to a strip below the canvas.

Result: `ViewportToolbar.tsx` (a hook returning the navigation and size groups, plus a
`ToolbarStrip`), a `useMediaQuery` hook deciding where the size group renders (no duplicate ids),
`ViewportCard` removed, the right column keeps Simulation and Seed only. The pan pad CSS is gone.
Test pages now use a 1280 x 1400 viewport: the extra strip made Playwright scroll, and the sticky
edit banner then covered the canvas corner a test clicks (diagnosed with elementFromPoint).

### 16. No vertical scrolling for the canvas column

> Can you make sure that no vertical scrolling is needed to view the col-lg-8? Can you say what
> your approach would be fore writing any code?

> ok, go ahead

Approach agreed first: a fixed-height flex page (navbar, then `main` filling the rest and scrolling
on its own for pages that need it, like the seed editor); the canvas takes the height that is left
instead of dictating it (the drawing already centres a square grid in any rectangle); the sidebar
scrolls by itself from lg up; a fixed canvas height below lg; the two hint lines merged into one.

Result: layout and CSS as planned, the canvas absolutely positioned inside its wrapper (an in-flow
canvas with height 100% feeds its bitmap size back into the layout), the edit banner compacted
(shorter copy, small text, thin progress bar) because at 992 x 600 it alone took 184 px. New
`LayoutBrowserTests`: no document or main overflow with the banner showing at 1280 x 720 and
992 x 600, every control inside the viewport, and a 500 px-tall window where the sidebar, not the
page, scrolls. The test fixture is back to Playwright's default viewport.

### 17. Canvas and toolbars only

> the seed card doesn't need to be visible all the time, only when the simulation doesn't run
> (initial state), or it is reset. Do you see UX problems with this goal, or not? The overarching
> goal I'm slowing going for is for the simulation canvas and toolbars to be the only UI visible
> under normal conditions.

> go ahead, do it in one pass

Assessment given first: visibility driven by shared state lets other clients yank the seed card
away mid-upload; generation 0 is too narrow a condition; export would be hidden when it matters;
discoverability suffers. Agreed remedy: availability follows shared state, visibility follows the
user's intent.

Result: the sidebar is gone. `SimulationControls` (icon buttons, Edit with its explained-disabled
wrapper, compact speed slider) and the compact `StatusBar` share the top strip; navigation, grid
size, a Seed button and an Export link share the bottom strip (one wrapping strip below lg).
`SeedOffcanvas` (react-bootstrap Offcanvas) holds the upload form and the editor link; its form is
disabled with an explanation while running or editing, but the panel never closes on its own. Shared
`IconButton` / `ExplainedWhenDisabled`. New `SeedBrowserTests` (panel opens/closes, disabled with
tooltip while running, stays open but disables when someone starts, export blocked only while
editing, nothing but canvas and two strips visible); layout tests cover 1280 x 500 and 600 x 900.

### 18. Dismissible gesture hint

> let's turn "Drag to pan, scroll or pinch to zoom. Keyboard: arrows pan, + and - zoom, Home
> recentres." into a dismissible alert, so that the user can close it. Store its state in
> localStorage (that is, don't show it if a given browser has had it closed. Before doing this,
> comfirm if this goes against UX best practices

Confirmed as an accepted pattern with two guards: the information must stay reachable (a help
button in the toolbar re-shows the hint and clears the stored dismissal) and state-dependent text
must not be bundled with it (the edit hint stays a separate line). The storage key is versioned so
a reworded hint appears once more; storage failures default to showing.

Result: `useStoredFlag` hook, `GestureHint` (Bootstrap dismissible alert), `#btn-help` in the
toolbar, `HintBrowserTests` (first visit shows it; dismissal survives reload; help brings it back
and forgets the dismissal).

### 19. Centred toolbar icons

> Can you take a look at the tooltip buttons? The icons don't look centered. Can you take a
> screenshot and verify? It looks most visible in .viewer-toolbar

Verified with Playwright screenshots at 3x device scale (the Chrome extension was not connected):
inline SVG icons sat on the text baseline, visibly above the buttons' centre. Fix: toolbar buttons
lay out their content with inline-flex, centred both ways, line-height 1 and a min-height equal to
a text-only small button, so icon-only and icon-plus-text buttons share one height. The strips got
ids (`#toolbar-top`, `#toolbar-bottom`) for reliable targeting. Re-captured crops show the icons
centred.

### 20. Gesture hint close button

> Can you take a look a #gesture-hint. When visible, the closing x is not style correctly. It seems
> that the close button wants more space than the div has.

Measured with Playwright: the alert was 39 px tall, Bootstrap's absolutely positioned
`.alert-dismissible .btn-close` (1.25 rem vertical padding) 54 px, overflowing by 16 px. Fix: the
hint is a flex row (`d-flex align-items-center`) with an in-flow close button instead of
`alert-dismissible`, so the button is centred and no taller than the alert.

### 21. UX audit, first pass

> before that, analyze the ui and look for UX inconsistencies, then report

> Ok, go with the order you suggested

Audit (from headless screenshots of nine states plus a code read) reported contradictions,
inconsistent patterns, wording and visual signals. This pass takes the suggested order:

- Dark mode: `data-bs-theme="auto"` is not a Bootstrap value, so the page was always light. An
  inline head script now sets the theme from the OS preference before first paint and the shell
  script keeps it in step; the canvas redraws on the theme attribute (MutationObserver) instead of
  the media query.
- Error channel: hub refusals and warnings are transient toasts over the canvas (`#notice`,
  auto-hide) instead of overwriting the connection badge.
- Export: wrapped in the explained-when-disabled pattern so its reason shows while editing.
- Grid size: out-of-range sides are clamped client-side with a notice naming the limits.
- Start/Pause no longer carry the pressed (`active`) style while disabled.
- The viewport outline uses the border colour, not danger red.
- Canvas overlays for what an empty-looking canvas cannot say: empty universe (Seed it / Draw
  cells), pattern outside the view (Recentre), tiny pattern (Zoom in, which recentres on the
  visible cells and shrinks the viewport around them).

`FeedbackBrowserTests` cover each. Wording and the instruction rework remain for a later pass.

### 22. One run/pause button

> Let's combine the play/pause button into one.

Result: `#btn-run` shows Play (green, "Start") while paused and Pause (yellow, "Pause") while
running; it is disabled while editing blocks a start. Tests that addressed `#btn-start` and
`#btn-pause` now address the one button and assert its accessible name for the state.

### 23. Navigation pad over the canvas

> Let's discuss: should pan buttons be assitance-only visible? The main method of interaction is
> mouse/touch.

> What about an overlay that is circle-shaped over the canvas? Or even, no necessarily an overlay,
> but the buttons are layed out so that directions make sense (up is up, etc)?

> I agree with your suggestion with overlay

Discussion: the pan/zoom buttons are the required single-pointer alternative to drag and pinch
(WCAG 2.5.1, 2.5.7), so they must stay visible to sighted users; hiding them for assistive
technology only helps nobody who needs them. A row breaks stimulus-response mapping; a cross of
real buttons keeps it without wedge hit-testing; a corner overlay follows the map-control
convention.

Result: `NavigationPad` (cross with Recentre in the middle, zoom stacked beside, bottom-right of
the canvas, translucent until hover/focus, 44 px targets on coarse pointers, only the buttons take
pointer events). The bottom strip is gone: grid size and the seed tools moved into the top strip;
notices moved to the top-right corner; `useMediaQuery` removed. `NavigationBrowserTests` check
placement, direction geometry, single strip, panning off and back into view, and touch sizing.

### 24. Pan left, zoom right

> lets move the two sets of controls - pan/centre and zoom to the two sides. What is common with
> video games on phones? There, usually one of the virtual pads is used for camera, the other for
> movement. If there are better practices for left/right placements, tell me about it

Practice: twin-stick games put the continuous directional input under the left thumb and discrete
or precise input under the right, following handedness; map apps keep zoom on the right and leave
the left free for dragging; bottom corners are the easiest thumb reach; controls stay inset from
the edge-swipe zones; a mirror option serves left-handed users (offered as a follow-up).

Result: the pan cross (with Recentre) floats bottom-left, the zoom stack bottom-right, both inset
1 rem, sharing a baseline. Tests check both clusters' placement, the edge inset, the shared
baseline, and the direction geometry.

### 25. Pad icon vanishing on hover

> When I click the zoom button and leave the mouse inside, the icon disappears. Can you look into it/

Cause (measured: colour and background both rgb(255,255,255) with the button focused): the pad
buttons used the `bg-body` utility to be opaque over the canvas, and its `!important` background
kept winning while the outline button's hover/focus state switched the icon to white. Fix: the rest
background is set through Bootstrap's `--bs-btn-bg` variable instead, so hover, focus and active
states keep their own contrasting colours. A regression test checks the button's colour differs
from its background at rest, hovered, and focused after a click.

### 26. "Seed" wording

> Do you think the text for the seed button can be improved?

> Ok, apply the change

"Seed" was a bare noun on an action button, hid that a panel follows, and was overloaded across the
page (the pattern that goes in versus the initial state Reset returns to). Applied: "Load pattern…"
(ellipsis: a panel follows), panel "Load a pattern", "Upload an .rle file" / Load, "Draw a
pattern", empty state "Load a pattern", Reset "Back to the initial state, generation 0", Recentre
"Back to the centre of the pattern", nav "Pattern editor", editor submit "Use as initial state",
server messages to match. Element ids are unchanged.

### 27. Instructions where the action is

> the text `Click Edit cells, then click cells in the view to flip them.` is still visible by default

The instruction rework discussed earlier, applied: the persistent hint line is gone; the enabled
Edit cells button (and Load pattern…) explain their purpose on hover via `ExplainedWhenDisabled`,
which now carries a tip for the enabled state too; while editing, a caption on the canvas
(`#canvas-editing`) says how; the one-time tip mentions drawing and points at Help; Help opens a
reference panel (`HelpOffcanvas`: moving, keyboard, editing, patterns) with a "Show the quick tip
again" button when the tip is dismissed. The tip's storage key moved to v2 so it shows once more.

### 28. One zoom variable

> now let's discuss the .viewport-size UI. This mirrors the zoom controls. First, the Apply button is
> no longer necessary. With some throttling, we can bind it to the current zoom level. The Grid label
> should not be necessary. I think it should be close to the zoom controls. What are best UX
> practices when it comes to this kind of duplicate controls?

> I don't see hard requiremnts for randome rect size in @TASK.md , so I think a single value offers
> the best UX

Practice: controls for one variable sit together; the precise one is the readout of the coarse one
(a stepper field); no Apply for a single reversible value (commit on Enter/blur, debounce typing);
label in the tooltip and accessible name; and decide whether it is one variable or two.

Result: one variable, cells across the view. The zoom cluster is a stepper: zoom out, an editable
number (`#cells-across`, Enter/blur/500 ms debounce, Shift+arrows step by ten, clamped with a
notice), zoom in. The height is derived from the canvas's aspect so the grid fills the canvas, and
the view re-requests a matching height 250 ms after the canvas changes shape. The grid-size input
group and Apply are gone from the toolbar (`ViewportToolbar.tsx` replaced by `ToolbarStrip.tsx`).
Tests now assert on the width and compute click targets from the reported view size.
Also found on the way: the canvas message panels (empty, off-screen, tiny, editing) intercepted
clicks on the cells beneath them; they now let pointer events through and only their buttons take
clicks, so the editing caption never blocks a cell.

### 29. Tooltips on the status figures

> Ok, when I enter a value, e.g. 30, on a resolution of 1184x919, the view results in 30x20
> according to the label on #status-viewport.

> Ok, but a tip on #status-viewport that explains what this means

Explained as intended (the height follows the canvas's shape). Added tooltips to the three status
figures: View ("cells across × down; the zoom controls set the width, the height follows the shape
of the canvas"), Gen (generations since the initial state) and Pop (live cells in the whole
universe, not only in the view). The figures are focusable so keyboard users get the tooltips too.

### 30. Toolbar regrouped; readouts as a head-up display

> Can you take a look at the top of the toolbar and give suggestions about the UX - what can be
> rearranged, so that it is more logical, e.g. things that work together are grouped together

> Let's discuss: what if the current `d-flex flex-wrap align-items-center gap-2 small ms-auto` group
> is turned into an overlay on the top center of the canvas? Then, the buttons that do most of the
> changes (e,g. edit cells, load pattern, help - all either redirect, or show offcanvas) are to the
> end (right on most screens).

> I agree with your feedback, proceed

Result: `CanvasHud` (top centre of the canvas, pass-through pointer events except on the figures,
tabular numbers with reserved widths, connection badge only while unhealthy, run state as a hidden
`aria-live` region). Strip: transport group (run/pause, step, reset set apart, speed with a unit
tooltip), a rule, Edit cells; flexible space; Load pattern…, Save .rle (now with a word), a rule,
Help. Groups are nowrap flex boxes so the strip wraps between groups. Toasts moved to the bottom
centre. `StatusBar.tsx` removed.
Follow-up: at phone widths the top-left prompt collided with the HUD; prompts now sit below the
HUD row under 768 px, and the narrow layout test asserts the two boxes do not overlap.

### 31. Theme switch

> Ok, before that. Can you add a toolbar button that switches dark mode on demand? It requires only
> an icon and a tooltip. I think it sould be placed before the help button. If you don't find UX
> problems with that, implement it, and then reword the commit message summary if necessary

One refinement: a two-state toggle would silently discard "follow the system", so the button cycles
light, dark, system (as Bootstrap's docs do), the icon shows the current mode and the tooltip names
the next one. `shared/theme.ts` owns the rules (stored under `gol.theme`, applied as the Bootstrap
theme attribute); the shell applies it on every page and follows the OS while in system mode; the
layout's inline head script applies the stored choice before first paint. `ThemeButton` sits before
Help. A test cycles the modes, checks persistence across pages and reloads, and the return to system.

### 32. Tooltips follow a mode switch

> Can you force-redraw the tooltip, if visible? If I have the tooltip opened, then click the button,
> the old tooltip is displayed, which can be confusing. Also, is there an icon that is typical for
> system-defined?

`IconButton` now controls its tooltip's visibility and keys the tooltip element on its text, so a
click that changes the text re-renders the open tooltip at once. The theme test hovers, clicks
twice and checks the tooltip names the next mode each time. Icon: `circle-half` is the convention
Bootstrap's own theme switcher uses for "auto"; kept.

### 33. Performance: benchmarks and baseline

> Ok, lets move to performance. Since we create a lot of objects to guarantee immunity, we need to
> start working on that. What's the best practices to benchmark memory usage, so that we can test
> if we've made meaningful progress?

> Ok, set up the benchmark project and capture the baseline

Practice: allocations per operation (BenchmarkDotNet MemoryDiagnoser; exact and portable, so also
assertable as unit-test budgets in CI) versus steady-state process memory (dotnet-counters,
dotnet-gcdump); Release only; timings never gated.

Result: `benchmarks/GameOfLife.Benchmarks` (engine step and snapshot on gun@1000, acorn@5000 and a
50k soup; viewport projection at 100 and 500; frame JSON; whole tick with 1/4/16 clients),
baseline in `benchmarks/results/2026-09-17-baseline.md`, and `AllocationBudgetTests` in Core
(steady-population stepping allocates nothing; snapshot is 16 B per cell; projection budgets).
Findings: stepping is already allocation-free; the soup's step alone takes 13 ms, so the engine,
not the clients, caps the generation rate; per client the costs are projection walking the whole
population and JSON text. The tick benchmark needed `invocationCount: 8` for stable numbers, and
the stepping budget excludes growing populations (the gun's sets double their capacity now and
then).

### 34. Optimisation pass: source generation, per-client buffers, packed cells

> Lets start with SourceGenerationContext, since the change is small. Then, let's make sure clients
> reuse the same backing array per client. Finally, let's change the frame's transport format for
> Cells. In between each change, say if we have improvemnts, and also, do so in the end.

(Preceded by three discussions: ArrayPool versus an owned growable buffer, a fixed 500 × 500 buffer
versus growing by visible count, and JSON source generation versus the payload format.)

1. `WireJsonContext` registered on SignalR's JSON protocol. Lesson: touching the resolver chain
   drops the implicit reflection resolver, which broke binding of `long` hub arguments until it was
   added back explicitly; a test now checks both. Gain: ~4 % serialisation time, same bytes.
2. Each connection owns a growable projection buffer reused by the broadcast; `Viewport.Project`
   gained a span-based, allocation-free overload (the enumerable overload allocated an enumerator).
   Frames from hub methods keep their own arrays. Gain: projection allocates nothing; the 16-client
   acorn tick fell from 62 KB to 27 KB and from 750 to 446 μs.
3. `CellsCodec`: cells travel as a tagged base64 string, delta-coded indices when sparse, a bitmap
   when at least one cell in twelve is alive; the client decodes to indices (`cells.ts`). The
   encoder writes into a per-connection scratch buffer, so only the string is allocated.
   Gain: a 100 × 100 frame of the soup is 1.8 KB instead of 10 KB and serialises in 0.3 μs instead
   of 10; a dense 500 × 500 frame is 41 KB instead of 321 KB (0.11 of the time). Whole tick with 16
   clients on the acorn: 750 → 327 μs and 62 → 25 KB. Tables in
   `benchmarks/results/2026-09-17-optimisations.md`. What remains is the engine: the snapshot copy
   and the step itself.
