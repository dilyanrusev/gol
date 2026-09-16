import { decodeRle, encodeRle } from "./rle.js";

/** The 100 x 100 seed editor: a matrix of real <button>s kept in sync with an RLE textarea. */
const PRESETS: Record<string, string> = {
  glider: "x = 3, y = 3\nbob$2bo$3o!",
  gun: "x = 36, y = 9\n24bo$22bobo$12b2o6b2o12b2o$11bo3bo4b2o12b2o$2o8bo5bo3b2o$2o8bo3bob2o4bobo$10bo5bo7bo$11bo3bo$12b2o!",
  pulsar: "x = 13, y = 13\n2b3o3b3o2b2$o4bobo4bo$o4bobo4bo$o4bobo4bo$2b3o3b3o2b2$2b3o3b3o2b$o4bobo4bo$o4bobo4bo$o4bobo4bo2$2b3o3b3o!",
  rpentomino: "x = 3, y = 3\nb2o$2o$bo!",
  acorn: "x = 7, y = 3\nbo5b$3bo3b$2o2b3o!",
};

const grid = document.getElementById("editor-grid") as HTMLDivElement;
const textarea = document.getElementById("editor-rle") as HTMLTextAreaElement;
const countLabel = document.getElementById("editor-count")!;
const nameInput = document.querySelector<HTMLInputElement>("input[name=Name]");
const size = Number(grid.dataset.size ?? 100);
grid.style.setProperty("--editor-size", String(size));

const buttons: HTMLButtonElement[] = [];
const alive = new Uint8Array(size * size);
let liveCount = 0;

const fragment = document.createDocumentFragment();
for (let y = 0; y < size; y++) {
  const row = document.createElement("div");
  row.setAttribute("role", "row");
  row.style.display = "contents";
  for (let x = 0; x < size; x++) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "cell";
    b.setAttribute("role", "gridcell");
    b.setAttribute("aria-pressed", "false");
    b.setAttribute("aria-label", `cell ${x + 1}, ${y + 1}`);
    b.tabIndex = x === 0 && y === 0 ? 0 : -1;
    b.dataset.i = String(y * size + x);
    buttons.push(b);
    row.appendChild(b);
  }
  fragment.appendChild(row);
}
grid.appendChild(fragment);

function setCell(i: number, on: boolean): void {
  if (alive[i] === (on ? 1 : 0)) return;
  alive[i] = on ? 1 : 0;
  liveCount += on ? 1 : -1;
  buttons[i].setAttribute("aria-pressed", on ? "true" : "false");
}

function syncTextarea(): void {
  const cells: Array<[number, number]> = [];
  for (let i = 0; i < alive.length; i++) if (alive[i]) cells.push([i % size, Math.floor(i / size)]);
  textarea.value = encodeRle(cells, nameInput?.value || undefined);
  countLabel.textContent = String(liveCount);
}

function clearAll(): void {
  for (let i = 0; i < alive.length; i++) setCell(i, false);
}

/** Loads a pattern into the grid, centred; leaves the grid alone if the text is invalid or too big. */
function loadRle(text: string): void {
  let pattern;
  try {
    pattern = decodeRle(text);
  } catch (err) {
    alert((err as Error).message);
    return;
  }
  if (pattern.width > size || pattern.height > size) {
    alert(`The pattern is ${pattern.width} x ${pattern.height}; it must fit in ${size} x ${size}.`);
    return;
  }
  clearAll();
  const ox = Math.floor((size - pattern.width) / 2);
  const oy = Math.floor((size - pattern.height) / 2);
  for (const [x, y] of pattern.cells) setCell((y + oy) * size + (x + ox), true);
  syncTextarea();
}

// --- mouse / touch painting: press toggles the first cell, dragging paints the same state ---
let paintState: boolean | null = null;

grid.addEventListener("pointerdown", (e) => {
  const target = e.target as HTMLElement;
  if (!target.dataset.i) return;
  const i = Number(target.dataset.i);
  paintState = !alive[i];
  setCell(i, paintState);
  (target as HTMLButtonElement).focus({ preventScroll: true });
  e.preventDefault();
});

grid.addEventListener("pointermove", (e) => {
  if (paintState === null) return;
  const el = document.elementFromPoint(e.clientX, e.clientY) as HTMLElement | null;
  if (el?.dataset.i) setCell(Number(el.dataset.i), paintState);
});

const stopPainting = () => {
  if (paintState === null) return;
  paintState = null;
  syncTextarea();
};
window.addEventListener("pointerup", stopPainting);
window.addEventListener("pointercancel", stopPainting);

// --- keyboard: arrows move focus (roving tabindex), Space/Enter toggle ---
grid.addEventListener("keydown", (e) => {
  const target = e.target as HTMLElement;
  if (!target.dataset.i) return;
  const i = Number(target.dataset.i);
  const x = i % size;
  const y = Math.floor(i / size);
  let nx = x;
  let ny = y;
  switch (e.key) {
    case "ArrowLeft": nx = Math.max(0, x - 1); break;
    case "ArrowRight": nx = Math.min(size - 1, x + 1); break;
    case "ArrowUp": ny = Math.max(0, y - 1); break;
    case "ArrowDown": ny = Math.min(size - 1, y + 1); break;
    case "Home": nx = 0; break;
    case "End": nx = size - 1; break;
    case "PageUp": ny = 0; break;
    case "PageDown": ny = size - 1; break;
    case " ":
    case "Enter":
      setCell(i, !alive[i]);
      syncTextarea();
      e.preventDefault();
      return;
    default: return;
  }
  e.preventDefault();
  buttons[i].tabIndex = -1;
  const next = buttons[ny * size + nx];
  next.tabIndex = 0;
  next.focus({ preventScroll: true });
});

document.getElementById("btn-clear")!.addEventListener("click", () => { clearAll(); syncTextarea(); });
document.getElementById("btn-apply-rle")!.addEventListener("click", () => loadRle(textarea.value));
document.querySelectorAll<HTMLButtonElement>("[data-preset]").forEach((b) =>
  b.addEventListener("click", () => loadRle(PRESETS[b.dataset.preset!])));
nameInput?.addEventListener("input", syncTextarea);

const initial = grid.dataset.initialRle?.trim() || textarea.value.trim();
if (initial) loadRle(initial); else syncTextarea();
