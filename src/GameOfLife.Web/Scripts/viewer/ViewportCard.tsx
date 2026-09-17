import { useEffect, useState, type ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import { Bullseye, CaretDownFill, CaretLeftFill, CaretRightFill, CaretUpFill, ZoomIn, ZoomOut } from "react-bootstrap-icons";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { useViewerConfig } from "./ViewerConfigContext";

export interface ViewportCardProps {
  frame: Frame | null;
  ready: boolean;
  onPan(dx: number, dy: number): void;
  onZoom(factor: number): void;
  onRecentre(): void;
  onResize(width: number, height: number): void;
}

/** An icon button with a tooltip. The label doubles as the accessible name, since the icon has none. */
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

/** The controls that affect only this client: pan, recentre, grid size, zoom. */
export function ViewportCard({ frame, ready, onPan, onZoom, onRecentre, onResize }: ViewportCardProps) {
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

  return (
    <div className="card mb-3">
      <div className="card-header">Viewport <small className="text-body-secondary">(this client only)</small></div>
      <div className="card-body">
        <div className="d-flex justify-content-center mb-3">
          <div className="pan-pad" role="group" aria-label="Pan">
            <span />
            {panButton(0, -1, "Pan up", <CaretUpFill aria-hidden />)}
            <span />
            {panButton(-1, 0, "Pan left", <CaretLeftFill aria-hidden />)}
            <IconButton id="btn-recentre" label="Recentre on seed" tip="Back to the centre of the seed pattern" disabled={!ready} onClick={onRecentre}>
              <Bullseye aria-hidden />
            </IconButton>
            {panButton(1, 0, "Pan right", <CaretRightFill aria-hidden />)}
            <span />
            {panButton(0, 1, "Pan down", <CaretDownFill aria-hidden />)}
            <span />
          </div>
        </div>
        <div className="row g-2 align-items-end">
          <div className="col-4">
            <label htmlFor="grid-width" className="form-label">Width</label>
            <input type="number" className="form-control" id="grid-width" min={config.minGridSize} max={config.maxGridSize}
                   value={widthDraft ?? String(width)} disabled={!ready} onChange={(e) => setWidthDraft(e.target.value)} />
          </div>
          <div className="col-4">
            <label htmlFor="grid-height" className="form-label">Height</label>
            <input type="number" className="form-control" id="grid-height" min={config.minGridSize} max={config.maxGridSize}
                   value={heightDraft ?? String(height)} disabled={!ready} onChange={(e) => setHeightDraft(e.target.value)} />
          </div>
          <div className="col-4">
            <button type="button" className="btn btn-outline-primary w-100" id="btn-resize" disabled={!ready} onClick={apply}>Apply</button>
          </div>
        </div>
        <div className="btn-group w-100 mt-2" role="group" aria-label="Zoom">
          <IconButton id="btn-zoom-in" label="Zoom in" tip="Show fewer cells, larger" disabled={!ready} onClick={() => onZoom(0.8)}>
            <ZoomIn aria-hidden /> Zoom in
          </IconButton>
          <IconButton id="btn-zoom-out" label="Zoom out" tip="Show more cells, smaller" disabled={!ready} onClick={() => onZoom(1.25)}>
            <ZoomOut aria-hidden /> Zoom out
          </IconButton>
        </div>
        <p className="form-text mb-0">Grid size is limited to {config.minGridSize}–{config.maxGridSize} cells per side.</p>
      </div>
    </div>
  );
}
