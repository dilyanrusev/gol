/**
 * Pointer-based pan and zoom for the viewport canvas: mouse drag / touch drag to pan,
 * wheel or pinch to zoom, a tap or click without movement to pick a cell, plus keyboard.
 * Emits whole-cell pans, zoom factors and canvas pixel positions only; it never knows
 * anything about universe coordinates.
 */
export interface GestureHandlers {
  /** The view should move by (dx, dy) whole cells (positive = content dragged up/left). */
  pan(dx: number, dy: number): void;
  /** The grid should shrink (factor < 1, zoom in) or grow (factor > 1, zoom out). */
  zoom(factor: number): void;
  recentre(): void;
  /** A click or tap that did not turn into a drag or pinch, in CSS pixels from the canvas's top-left. */
  tap?(x: number, y: number): void;
  /** Current size of one cell in CSS pixels; used to convert pointer movement into cells. */
  cellSize(): number;
}

/** Pointer travel (CSS px) beyond which a press counts as a drag rather than a tap. */
const TAP_SLOP_PX = 5;

/** Attaches the gesture listeners and returns a function that removes them again. */
export function attachGestures(canvas: HTMLCanvasElement, handlers: GestureHandlers): () => void {
  const pointers = new Map<number, { x: number; y: number; startX: number; startY: number; moved: boolean }>();
  let remainderX = 0;
  let remainderY = 0;
  let pinchDistance = 0;
  const controller = new AbortController();
  const { signal } = controller;

  const flushPan = (dxPx: number, dyPx: number) => {
    const size = Math.max(handlers.cellSize(), 1e-6);
    // Dragging the content right means the viewport origin moves left.
    remainderX -= dxPx / size;
    remainderY -= dyPx / size;
    const dx = Math.trunc(remainderX);
    const dy = Math.trunc(remainderY);
    if (dx !== 0 || dy !== 0) {
      remainderX -= dx;
      remainderY -= dy;
      handlers.pan(dx, dy);
    }
  };

  const distance = () => {
    const [a, b] = [...pointers.values()];
    return Math.hypot(a.x - b.x, a.y - b.y);
  };

  canvas.addEventListener("pointerdown", (e) => {
    canvas.setPointerCapture(e.pointerId);
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY, startX: e.clientX, startY: e.clientY, moved: false });
    if (pointers.size === 2) {
      pinchDistance = distance();
      // Two fingers are a pinch, never a tap.
      for (const p of pointers.values()) p.moved = true;
    }
    canvas.focus({ preventScroll: true });
    e.preventDefault();
  }, { signal });

  canvas.addEventListener("pointermove", (e) => {
    const p = pointers.get(e.pointerId);
    if (!p) return;
    const dx = e.clientX - p.x;
    const dy = e.clientY - p.y;
    p.x = e.clientX;
    p.y = e.clientY;
    if (!p.moved && Math.hypot(p.x - p.startX, p.y - p.startY) > TAP_SLOP_PX) p.moved = true;

    if (pointers.size === 1) {
      flushPan(dx, dy);
    } else if (pointers.size === 2) {
      const d = distance();
      if (pinchDistance > 0 && Math.abs(d - pinchDistance) > 12) {
        handlers.zoom(d > pinchDistance ? 0.8 : 1.25);
        pinchDistance = d;
      }
    }
  }, { signal });

  canvas.addEventListener("pointerup", (e) => {
    const p = pointers.get(e.pointerId);
    pointers.delete(e.pointerId);
    if (pointers.size < 2) pinchDistance = 0;
    if (p && !p.moved && handlers.tap) {
      const rect = canvas.getBoundingClientRect();
      handlers.tap(e.clientX - rect.left, e.clientY - rect.top);
    }
  }, { signal });

  canvas.addEventListener("pointercancel", (e) => {
    pointers.delete(e.pointerId);
    if (pointers.size < 2) pinchDistance = 0;
  }, { signal });

  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    handlers.zoom(e.deltaY < 0 ? 0.8 : 1.25);
  }, { passive: false, signal });

  canvas.addEventListener("keydown", (e) => {
    const step = e.shiftKey ? 10 : 1;
    switch (e.key) {
      case "ArrowLeft": handlers.pan(-step, 0); break;
      case "ArrowRight": handlers.pan(step, 0); break;
      case "ArrowUp": handlers.pan(0, -step); break;
      case "ArrowDown": handlers.pan(0, step); break;
      case "+": case "=": handlers.zoom(0.8); break;
      case "-": case "_": handlers.zoom(1.25); break;
      case "Home": handlers.recentre(); break;
      default: return;
    }
    e.preventDefault();
  }, { signal });

  return () => controller.abort();
}
