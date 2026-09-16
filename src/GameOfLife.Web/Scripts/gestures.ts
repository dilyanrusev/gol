/**
 * Pointer-based pan and zoom for the viewport canvas: mouse drag / touch drag to pan,
 * wheel or pinch to zoom, plus keyboard. Emits whole-cell pans and zoom factors only;
 * it never knows anything about universe coordinates.
 */
export interface GestureHandlers {
  /** The view should move by (dx, dy) whole cells (positive = content dragged up/left). */
  pan(dx: number, dy: number): void;
  /** The grid should shrink (factor < 1, zoom in) or grow (factor > 1, zoom out). */
  zoom(factor: number): void;
  recentre(): void;
  /** Current size of one cell in CSS pixels; used to convert pointer movement into cells. */
  cellSize(): number;
}

export function attachGestures(canvas: HTMLCanvasElement, handlers: GestureHandlers): void {
  const pointers = new Map<number, { x: number; y: number }>();
  let remainderX = 0;
  let remainderY = 0;
  let pinchDistance = 0;

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
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (pointers.size === 2) pinchDistance = distance();
    canvas.focus({ preventScroll: true });
    e.preventDefault();
  });

  canvas.addEventListener("pointermove", (e) => {
    const previous = pointers.get(e.pointerId);
    if (!previous) return;
    const current = { x: e.clientX, y: e.clientY };
    pointers.set(e.pointerId, current);

    if (pointers.size === 1) {
      flushPan(current.x - previous.x, current.y - previous.y);
    } else if (pointers.size === 2) {
      const d = distance();
      if (pinchDistance > 0 && Math.abs(d - pinchDistance) > 12) {
        handlers.zoom(d > pinchDistance ? 0.8 : 1.25);
        pinchDistance = d;
      }
    }
  });

  const release = (e: PointerEvent) => {
    pointers.delete(e.pointerId);
    if (pointers.size < 2) pinchDistance = 0;
  };
  canvas.addEventListener("pointerup", release);
  canvas.addEventListener("pointercancel", release);

  canvas.addEventListener("wheel", (e) => {
    e.preventDefault();
    handlers.zoom(e.deltaY < 0 ? 0.8 : 1.25);
  }, { passive: false });

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
  });
}
