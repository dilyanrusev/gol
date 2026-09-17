import type { ReactNode } from "react";

/**
 * A small panel in the canvas's top-left corner for situations the empty-looking canvas cannot
 * explain by itself: nothing to show, nothing in view, or a pattern too small to see. It never
 * blocks gestures outside its own box.
 */
export function CanvasOverlay({ id, children }: { id: string; children: ReactNode }) {
  return (
    <div className="viewer-overlay">
      <div id={id} className="bg-body bg-opacity-75 border rounded shadow-sm px-3 py-2 small d-flex flex-wrap align-items-center gap-2" role="status">
        {children}
      </div>
    </div>
  );
}
