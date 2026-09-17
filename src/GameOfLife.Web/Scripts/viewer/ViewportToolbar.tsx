import { useEffect, useState, type ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import { Bullseye, CaretDownFill, CaretLeftFill, CaretRightFill, CaretUpFill, ZoomIn, ZoomOut } from "react-bootstrap-icons";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { useViewerConfig } from "./ViewerConfigContext";

export interface ViewportToolbarProps {
  frame: Frame | null;
  ready: boolean;
  onPan(dx: number, dy: number): void;
  onZoom(factor: number): void;
  onRecentre(): void;
  onResize(width: number, height: number): void;
}

/** A small icon button with a tooltip. The label doubles as the accessible name, since the icon has none. */
function IconButton({ id, label, tip, disabled, onClick, children }: {
  id?: string; label: string; tip: string; disabled: boolean; onClick(): void; children: ReactNode;
}) {
  return (
    <OverlayTrigger placement="top" overlay={<Tooltip id={`tip-${id ?? label.replace(/\s+/g, "-").toLowerCase()}`}>{tip}</Tooltip>}>
      <button type="button" className="btn btn-outline-secondary" id={id} aria-label={label} disabled={disabled} onClick={onClick}>
        {children}
      </button>
    </OverlayTrigger>
  );
}

/**
 * The per-client viewport controls as two thin strips around the canvas. Gestures on the canvas
 * are the primary way to move around; these are the keyboard-and-mouse fallback.
 */
export function useViewportToolbar({ frame, ready, onPan, onZoom, onRecentre, onResize }: ViewportToolbarProps) {
  const config = useViewerConfig();
  const width = frame?.width ?? config.defaultGridSize;
  const height = frame?.height ?? config.defaultGridSize;

  // Typed sizes survive incoming frames until Apply is pressed or the server-side size changes.
  const [widthDraft, setWidthDraft] = useState<string | null>(null);
  const [heightDraft, setHeightDraft] = useState<string | null>(null);
  useEffect(() => { setWidthDraft(null); }, [width]);
  useEffect(() => { setHeightDraft(null); }, [height]);
  const apply = () => onResize(Number(widthDraft ?? width), Number(heightDraft ?? height));

  // A pan button moves by a tenth of the viewport, at least one cell.
  const stepX = Math.max(1, Math.round(width / 10));
  const stepY = Math.max(1, Math.round(height / 10));
  const panButton = (dx: number, dy: number, label: string, icon: ReactNode) => (
    <IconButton label={label} tip={`${label} by ${dx !== 0 ? stepX : stepY} cells`} disabled={!ready} onClick={() => onPan(dx * stepX, dy * stepY)}>
      {icon}
    </IconButton>
  );

  const navigation = (
    <>
      <div className="btn-group btn-group-sm" role="group" aria-label="Pan">
        {panButton(-1, 0, "Pan left", <CaretLeftFill aria-hidden />)}
        {panButton(0, -1, "Pan up", <CaretUpFill aria-hidden />)}
        {panButton(0, 1, "Pan down", <CaretDownFill aria-hidden />)}
        {panButton(1, 0, "Pan right", <CaretRightFill aria-hidden />)}
        <IconButton id="btn-recentre" label="Recentre on seed" tip="Back to the centre of the seed pattern" disabled={!ready} onClick={onRecentre}>
          <Bullseye aria-hidden />
        </IconButton>
      </div>
      <div className="btn-group btn-group-sm" role="group" aria-label="Zoom">
        <IconButton id="btn-zoom-in" label="Zoom in" tip="Show fewer cells, larger" disabled={!ready} onClick={() => onZoom(0.8)}>
          <ZoomIn aria-hidden />
        </IconButton>
        <IconButton id="btn-zoom-out" label="Zoom out" tip="Show more cells, smaller" disabled={!ready} onClick={() => onZoom(1.25)}>
          <ZoomOut aria-hidden />
        </IconButton>
      </div>
    </>
  );

  const size = (
    <div className="input-group input-group-sm viewport-size" role="group" aria-label="Grid size">
      <span className="input-group-text">Grid</span>
      <input type="number" className="form-control" id="grid-width" aria-label="Width in cells"
             min={config.minGridSize} max={config.maxGridSize}
             value={widthDraft ?? String(width)} disabled={!ready} onChange={(e) => setWidthDraft(e.target.value)} />
      <span className="input-group-text">×</span>
      <input type="number" className="form-control" id="grid-height" aria-label="Height in cells"
             min={config.minGridSize} max={config.maxGridSize}
             value={heightDraft ?? String(height)} disabled={!ready} onChange={(e) => setHeightDraft(e.target.value)} />
      <OverlayTrigger placement="top" overlay={<Tooltip id="tip-resize">Apply the grid size ({config.minGridSize}–{config.maxGridSize} cells per side)</Tooltip>}>
        <button type="button" className="btn btn-outline-primary" id="btn-resize" disabled={!ready} onClick={apply}>Apply</button>
      </OverlayTrigger>
    </div>
  );

  return { navigation, size };
}

/** A thin strip spanning the canvas width; contents wrap on narrow screens. */
export function ToolbarStrip({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <div className={`viewport-toolbar d-flex flex-wrap align-items-center gap-2 ${className}`}>{children}</div>;
}
