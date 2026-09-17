import type { ReactNode } from "react";

/** A thin strip spanning the canvas width; contents wrap on narrow screens. */
export function ToolbarStrip({ id, children, className = "" }: { id: string; children: ReactNode; className?: string }) {
  return <div id={id} className={`viewer-toolbar d-flex flex-wrap align-items-center gap-2 ${className}`}>{children}</div>;
}
