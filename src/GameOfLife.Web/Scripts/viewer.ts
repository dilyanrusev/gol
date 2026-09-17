import { attachGestures } from "./gestures.js";
// Generated from the server's ILifeHub / ILifeClient / Frame by the build (see GameOfLife.Web.csproj).
import { getHubProxyFactory, getReceiverRegister } from "./generated/TypedSignalR.Client/index.js";
import type { ILifeHub } from "./generated/TypedSignalR.Client/GameOfLife.Web.Hubs.js";
import type { Frame } from "./generated/GameOfLife.Web.Simulation.js";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>("universe");
const ctx = canvas.getContext("2d")!;
const gridWidth = $<HTMLInputElement>("grid-width");
const gridHeight = $<HTMLInputElement>("grid-height");
const speed = $<HTMLInputElement>("speed");
const btnStart = $<HTMLButtonElement>("btn-start");
const btnPause = $<HTMLButtonElement>("btn-pause");
const btnStep = $<HTMLButtonElement>("btn-step");
const btnReset = $<HTMLButtonElement>("btn-reset");
const btnEdit = $<HTMLButtonElement>("btn-edit");
// Explains a disabled Edit button. Lives on the wrapper because disabled buttons get no pointer events.
// A tooltip exists only while there is something to explain: Bootstrap re-creates a shown tooltip on
// every content change and can leave a hidden one behind if the pointer returns mid fade-out, so
// the instance is disposed and rebuilt only when the text changes.
const editTooltipHost = $("btn-edit-wrap");
let editTooltip: bootstrap.Tooltip | null = null;
let editTooltipText: string | null = null;

function setEditTooltip(text: string | null): void {
  if (text === editTooltipText) return;
  editTooltipText = text;
  editTooltip?.dispose();
  editTooltip = text ? new bootstrap.Tooltip(editTooltipHost, { title: text, placement: "bottom" }) : null;
}
const btnEditDone = $<HTMLButtonElement>("btn-edit-done");
const btnEditCancel = $<HTMLButtonElement>("btn-edit-cancel");
const editBanner = $("edit-banner");
const editProgressBar = $("edit-progress-bar");

/** Cells smaller than this (CSS px) cannot be targeted reliably, so clicks are refused until the user zooms in. */
const MIN_EDIT_CELL_PX = 4;

// Placeholder until the hub sends the real state; nothing below trusts it as the world's state.
let frame: Frame = {
  generation: 0, population: 0, running: false, generationsPerSecond: 10, width: 100, height: 100, cells: [],
  editing: false, editingByMe: false, editRemainingMs: 0, editTimeoutMs: 0,
};
let cellPx = 1;
// Where the grid sits inside the canvas (CSS px), so a tap can be mapped back to a cell.
let gridOx = 0;
let gridOy = 0;

// ---------- rendering ----------
function render(): void {
  const dpr = window.devicePixelRatio || 1;
  const cssWidth = canvas.clientWidth;
  const cssHeight = canvas.clientHeight;
  if (canvas.width !== Math.round(cssWidth * dpr) || canvas.height !== Math.round(cssHeight * dpr)) {
    canvas.width = Math.round(cssWidth * dpr);
    canvas.height = Math.round(cssHeight * dpr);
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

  cellPx = Math.min(cssWidth / frame.width, cssHeight / frame.height);
  const gridW = cellPx * frame.width;
  const gridH = cellPx * frame.height;
  gridOx = (cssWidth - gridW) / 2;
  gridOy = (cssHeight - gridH) / 2;
  const ox = gridOx;
  const oy = gridOy;
  const style = getComputedStyle(document.documentElement);

  ctx.fillStyle = style.getPropertyValue("--bs-body-bg") || "#fff";
  ctx.fillRect(0, 0, cssWidth, cssHeight);

  ctx.fillStyle = style.getPropertyValue("--bs-primary") || "#0d6efd";
  const inset = cellPx >= 6 ? 1 : 0;
  for (const i of frame.cells) {
    const x = i % frame.width;
    const y = (i - x) / frame.width;
    ctx.fillRect(ox + x * cellPx + inset, oy + y * cellPx + inset, cellPx - inset, cellPx - inset);
  }

  if (cellPx >= 6) {
    ctx.strokeStyle = style.getPropertyValue("--bs-border-color-translucent") || "rgba(0,0,0,.1)";
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = 0; x <= frame.width; x++) { ctx.moveTo(ox + x * cellPx + 0.5, oy); ctx.lineTo(ox + x * cellPx + 0.5, oy + gridH); }
    for (let y = 0; y <= frame.height; y++) { ctx.moveTo(ox, oy + y * cellPx + 0.5); ctx.lineTo(ox + gridW, oy + y * cellPx + 0.5); }
    ctx.stroke();
  }

  // Mark the centre so the user can see where the seed centre is after recentring.
  ctx.strokeStyle = style.getPropertyValue("--bs-danger") || "#dc3545";
  ctx.strokeRect(ox + 0.5, oy + 0.5, gridW - 1, gridH - 1);
}

/** Makes the page reflect the world exactly as the hub describes it: counters, viewport, speed, run and edit state. */
function applyFrame(f: Frame): void {
  frame = f;
  $("status-generation").textContent = f.generation.toLocaleString();
  $("status-population").textContent = f.population.toLocaleString();
  $("status-viewport").textContent = `${f.width} × ${f.height}`;
  if (document.activeElement !== gridWidth) gridWidth.value = String(f.width);
  if (document.activeElement !== gridHeight) gridHeight.value = String(f.height);
  if (document.activeElement !== speed) { speed.value = String(f.generationsPerSecond); $("speed-value").textContent = speed.value; }
  speed.disabled = false;

  const runState = $("status-running");
  runState.textContent = f.running ? "running" : "paused";
  runState.className = `badge ${f.running ? "text-bg-success" : "text-bg-secondary"}`;
  btnStart.classList.toggle("active", f.running);
  btnPause.classList.toggle("active", !f.running);
  // While anyone edits, nothing may change the universe except the editor's clicks.
  btnStart.disabled = f.running || f.editing;
  btnPause.disabled = !f.running;
  btnStep.disabled = f.editing;
  btnReset.disabled = f.editing;

  applyEditState(f);
  render();
}

/** While there is no live connection the shared controls cannot act, so they are greyed out. */
function disableSimulationControls(): void {
  for (const button of [btnStart, btnPause, btnStep, btnReset, btnEdit, btnEditDone, btnEditCancel]) button.disabled = true;
  setEditTooltip(null);
  speed.disabled = true;
  editBanner.hidden = true;
  stopCountdown();
}

// ---------- editing ----------
// When the current edit session expires, on this page's clock. Frames carry the remaining time,
// so a fresh frame re-anchors it; between frames the countdown runs locally.
let editDeadline = 0;
let countdownTimer: number | null = null;

function applyEditState(f: Frame): void {
  btnEdit.disabled = f.running || f.editing;
  btnEdit.textContent = f.editingByMe ? "Editing…" : "Edit cells";
  setEditTooltip(f.running
    ? "Pause the simulation first, then you can edit cells."
    : f.editing && !f.editingByMe
      ? "Another client is editing. You can edit once they are done."
      : null);
  canvas.classList.toggle("editing", f.editingByMe);
  $("edit-hint").textContent = f.running
    ? "Pause the simulation to edit cells."
    : f.editing && !f.editingByMe
      ? "Another client is editing; you can edit once they are done."
      : f.editingByMe
        ? "Click or tap a cell to flip it. Drag still pans, scroll or pinch still zooms."
        : "Click Edit cells, then click cells in the view to flip them.";

  if (!f.editing) {
    editBanner.hidden = true;
    stopCountdown();
    return;
  }

  editDeadline = performance.now() + f.editRemainingMs;
  $("edit-banner-title").textContent = f.editingByMe ? "You are editing the universe." : "Another client is editing the universe.";
  $("edit-banner-text").textContent = f.editingByMe
    ? "Nobody can start, step or reset until you finish. Done keeps your edits and resumes the simulation; Cancel discards them and stays paused. Every click restarts the timer; when it runs out your edits are kept and the simulation stays paused."
    : "Start, Step and Reset are disabled for everyone until they finish or the timer runs out. Each of their edits restarts the timer.";
  $("edit-banner-actions").hidden = !f.editingByMe;
  btnEditDone.disabled = btnEditCancel.disabled = !f.editingByMe;
  editBanner.hidden = false;
  updateCountdown();
  if (countdownTimer === null) countdownTimer = window.setInterval(updateCountdown, 250);
}

function stopCountdown(): void {
  if (countdownTimer !== null) { window.clearInterval(countdownTimer); countdownTimer = null; }
}

/** Countdown text plus a bar that starts full and empties, coloured by how much of the timeout is left. */
function updateCountdown(): void {
  const remainingMs = Math.max(0, editDeadline - performance.now());
  const fraction = frame.editTimeoutMs > 0 ? Math.min(1, remainingMs / frame.editTimeoutMs) : 0;
  const totalSeconds = Math.ceil(remainingMs / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  $("edit-countdown").textContent = `${minutes}:${String(seconds).padStart(2, "0")}`;

  const level = fraction > 0.5 ? "success" : fraction > 0.2 ? "warning" : "danger";
  editProgressBar.style.width = `${fraction * 100}%`;
  editProgressBar.className = `progress-bar bg-${level}`;
  $("edit-progress").setAttribute("aria-valuenow", String(Math.round(fraction * 100)));
  editBanner.className = `alert alert-${level} sticky-top shadow mb-3`;
}

/** Maps a tap on the canvas to a viewport cell and asks the hub to flip it (editor only). */
function tap(px: number, py: number): void {
  if (!frame.editingByMe) return;
  if (cellPx < MIN_EDIT_CELL_PX) { setStatus("zoom in to edit: cells are too small to click", "text-bg-warning"); return; }
  const x = Math.floor((px - gridOx) / cellPx);
  const y = Math.floor((py - gridOy) / cellPx);
  if (x < 0 || y < 0 || x >= frame.width || y >= frame.height) return;
  void call("toggleCell", x, y);
}

new ResizeObserver(render).observe(canvas);
window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", render);

// ---------- connection ----------
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/life")
  .withAutomaticReconnect()
  .build();
const hub = getHubProxyFactory("ILifeHub").createHubProxy(connection);

const setStatus = (text: string, cls: string) => {
  const el = $("status-connection");
  el.textContent = text;
  el.className = `badge ${cls}`;
};

// A reconnect gets a new connection, and with it the server drops our edit session; ask for it back.
let resumeEditAfterReconnect = false;

/**
 * Pulls the current frame from the hub so the page starts from the world's actual state
 * (generation, population, running/paused, speed, viewport, editing) rather than the placeholder above.
 * Used on every (re)connect; the hub's own push on connect is not relied upon.
 */
async function initialiseFromHub(): Promise<void> {
  setStatus("connected", "text-bg-success");
  try {
    applyFrame(await hub.refresh());
  } catch (err) {
    console.error("refresh", err);
    setStatus("no state from server", "text-bg-danger");
    return;
  }
  if (resumeEditAfterReconnect) {
    resumeEditAfterReconnect = false;
    if (!frame.editing) await call("beginEdit");
    else if (!frame.editingByMe) setStatus("another client took over editing while you were reconnecting", "text-bg-warning");
  }
}

getReceiverRegister("ILifeClient").register(connection, { receiveFrame: async (f) => applyFrame(f) });
connection.onreconnecting(() => {
  resumeEditAfterReconnect = frame.editingByMe;
  setStatus("reconnecting…", "text-bg-warning");
  disableSimulationControls();
});
connection.onreconnected(initialiseFromHub);
connection.onclose(() => { setStatus("disconnected", "text-bg-danger"); disableSimulationControls(); });

/**
 * Invokes a hub method through the generated proxy, applies the frame it returns (if any) and
 * surfaces hub errors in the status badge. The method name and arguments are checked against
 * ILifeHub, so a renamed or re-typed server method fails to compile here.
 */
async function call<M extends keyof ILifeHub>(method: M, ...args: Parameters<ILifeHub[M]>): Promise<void> {
  const invoke = hub[method] as (...a: Parameters<ILifeHub[M]>) => Promise<Frame | void>;
  try {
    const result = await invoke(...args);
    if (result) applyFrame(result);
  } catch (err) {
    console.error(method, err);
    setStatus((err as Error).message.replace(/^.*HubException: /, ""), "text-bg-danger");
  }
}

// Viewport changes are coalesced: at most one in flight, the rest accumulate.
let pendingDx = 0;
let pendingDy = 0;
let inFlight: Promise<void> | null = null;

function pan(dx: number, dy: number): void {
  pendingDx += dx;
  pendingDy += dy;
  if (inFlight) return;
  inFlight = (async () => {
    while (pendingDx !== 0 || pendingDy !== 0) {
      const [x, y] = [pendingDx, pendingDy];
      pendingDx = pendingDy = 0;
      if (!Number.isSafeInteger(x) || !Number.isSafeInteger(y)) { setStatus("pan too large", "text-bg-danger"); break; }
      await call("pan", x, y);
    }
    inFlight = null;
  })();
}

const resize = (w: number, h: number) => call("resize", Math.round(w), Math.round(h));
const zoom = (factor: number) => resize(frame.width * factor, frame.height * factor);
const recentre = () => call("recentre");

attachGestures(canvas, { pan, zoom, recentre, tap, cellSize: () => cellPx });

// ---------- buttons ----------
btnStart.addEventListener("click", () => call("start"));
btnPause.addEventListener("click", () => call("pause"));
btnStep.addEventListener("click", () => call("step"));
btnReset.addEventListener("click", () => call("reset"));
speed.addEventListener("input", () => { $("speed-value").textContent = speed.value; });
speed.addEventListener("change", () => call("setSpeed", Number(speed.value)));

btnEdit.addEventListener("click", () => call("beginEdit"));
btnEditDone.addEventListener("click", () => call("endEdit"));
btnEditCancel.addEventListener("click", () => call("cancelEdit"));

$("btn-recentre").addEventListener("click", recentre);
$("btn-zoom-in").addEventListener("click", () => zoom(0.8));
$("btn-zoom-out").addEventListener("click", () => zoom(1.25));
$("btn-resize").addEventListener("click", () => resize(Number(gridWidth.value), Number(gridHeight.value)));
document.querySelectorAll<HTMLButtonElement>("[data-pan]").forEach((b) => {
  const [dx, dy] = b.dataset.pan!.split(",").map(Number);
  b.addEventListener("click", () => pan(dx * Math.max(1, Math.round(frame.width / 10)), dy * Math.max(1, Math.round(frame.height / 10))));
});

disableSimulationControls();
render();
connection.start()
  .then(initialiseFromHub)
  .catch((err) => { console.error(err); setStatus("connection failed", "text-bg-danger"); });
