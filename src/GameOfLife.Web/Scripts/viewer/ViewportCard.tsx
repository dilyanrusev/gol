import { useEffect, useState } from "react";
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
  const panButton = (dx: number, dy: number, label: string, glyph: string) => (
    <button type="button" className="btn btn-outline-secondary" aria-label={label} disabled={!ready}
            onClick={() => onPan(dx * stepX, dy * stepY)}>{glyph}</button>
  );

  return (
    <div className="card mb-3">
      <div className="card-header">Viewport <small className="text-body-secondary">(this client only)</small></div>
      <div className="card-body">
        <div className="d-flex justify-content-center mb-3">
          <div className="pan-pad" role="group" aria-label="Pan">
            <span />
            {panButton(0, -1, "Pan up", "▲")}
            <span />
            {panButton(-1, 0, "Pan left", "◀")}
            <button type="button" className="btn btn-outline-secondary" id="btn-recentre" aria-label="Recentre on seed"
                    disabled={!ready} onClick={onRecentre}>⌂</button>
            {panButton(1, 0, "Pan right", "▶")}
            <span />
            {panButton(0, 1, "Pan down", "▼")}
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
          <button type="button" className="btn btn-outline-secondary" id="btn-zoom-in" disabled={!ready} onClick={() => onZoom(0.8)}>Zoom in</button>
          <button type="button" className="btn btn-outline-secondary" id="btn-zoom-out" disabled={!ready} onClick={() => onZoom(1.25)}>Zoom out</button>
        </div>
        <p className="form-text mb-0">Grid size is limited to {config.minGridSize}–{config.maxGridSize} cells per side.</p>
      </div>
    </div>
  );
}
