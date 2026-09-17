import { attachGestures } from "./gestures.js";
// Generated from the server's ILifeHub / ILifeClient / Frame by the build (see GameOfLife.Web.csproj).
import { getHubProxyFactory, getReceiverRegister } from "./generated/TypedSignalR.Client/index.js";
import type { Frame } from "./generated/GameOfLife.Web.Simulation.js";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
const canvas = $<HTMLCanvasElement>("universe");
const ctx = canvas.getContext("2d")!;
const gridWidth = $<HTMLInputElement>("grid-width");
const gridHeight = $<HTMLInputElement>("grid-height");
const speed = $<HTMLInputElement>("speed");

// Placeholder until the hub sends the real state; nothing below trusts it as the world's state.
let frame: Frame = { generation: 0, population: 0, running: false, generationsPerSecond: 10, width: 100, height: 100, cells: [] };
let cellPx = 1;
const simulationControls = ["btn-start", "btn-pause", "btn-step", "btn-reset"].map((id) => $<HTMLButtonElement>(id));

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
  const ox = (cssWidth - gridW) / 2;
  const oy = (cssHeight - gridH) / 2;
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

/** Makes the page reflect the world exactly as the hub describes it: counters, viewport, speed and run state. */
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
  const start = $<HTMLButtonElement>("btn-start");
  const pause = $<HTMLButtonElement>("btn-pause");
  start.classList.toggle("active", f.running);
  pause.classList.toggle("active", !f.running);
  start.disabled = f.running;
  pause.disabled = !f.running;
  $<HTMLButtonElement>("btn-step").disabled = false;
  $<HTMLButtonElement>("btn-reset").disabled = false;
  render();
}

/** While there is no live connection the shared controls cannot act, so they are greyed out. */
function disableSimulationControls(): void {
  for (const button of simulationControls) button.disabled = true;
  speed.disabled = true;
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

/**
 * Pulls the current frame from the hub so the page starts from the world's actual state
 * (generation, population, running/paused, speed, viewport) rather than the placeholder above.
 * Used on every (re)connect; the hub's own push on connect is not relied upon.
 */
async function initialiseFromHub(): Promise<void> {
  setStatus("connected", "text-bg-success");
  try {
    applyFrame(await hub.refresh());
  } catch (err) {
    console.error("refresh", err);
    setStatus("no state from server", "text-bg-danger");
  }
}

getReceiverRegister("ILifeClient").register(connection, { receiveFrame: async (f) => applyFrame(f) });
connection.onreconnecting(() => { setStatus("reconnecting…", "text-bg-warning"); disableSimulationControls(); });
connection.onreconnected(initialiseFromHub);
connection.onclose(() => { setStatus("disconnected", "text-bg-danger"); disableSimulationControls(); });

/** Awaits a hub call, applies the frame it returns (if any) and surfaces hub errors in the status badge. */
async function call(name: string, request: Promise<Frame | void>): Promise<void> {
  try {
    const result = await request;
    if (result) applyFrame(result);
  } catch (err) {
    console.error(name, err);
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
      await call("pan", hub.pan(x, y));
    }
    inFlight = null;
  })();
}

const resize = (w: number, h: number) => call("resize", hub.resize(Math.round(w), Math.round(h)));
const zoom = (factor: number) => resize(frame.width * factor, frame.height * factor);
const recentre = () => call("recentre", hub.recentre());

attachGestures(canvas, { pan, zoom, recentre, cellSize: () => cellPx });

// ---------- buttons ----------
$("btn-start").addEventListener("click", () => call("start", hub.start()));
$("btn-pause").addEventListener("click", () => call("pause", hub.pause()));
$("btn-step").addEventListener("click", () => call("step", hub.step()));
$("btn-reset").addEventListener("click", () => call("reset", hub.reset()));
speed.addEventListener("input", () => { $("speed-value").textContent = speed.value; });
speed.addEventListener("change", () => call("setSpeed", hub.setSpeed(Number(speed.value))));

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
