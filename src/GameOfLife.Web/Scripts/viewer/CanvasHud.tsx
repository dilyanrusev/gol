import type { ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import type { Status } from "./useLifeHub";

/** A status figure with a tooltip saying what it measures. Focusable, so keyboard users get the tooltip too. */
function Figure({ id, label, tip, minChars, children }: { id: string; label: string; tip: string; minChars?: number; children: ReactNode }) {
  return (
    <OverlayTrigger placement="bottom" overlay={<Tooltip id={`tip-${id}`}>{tip}</Tooltip>}>
      <span className="text-nowrap viewer-hud-figure" tabIndex={0}>
        {label} <strong id={id} style={minChars ? { display: "inline-block", minWidth: `${minChars}ch`, textAlign: "right" } : undefined}>{children}</strong>
      </span>
    </OverlayTrigger>
  );
}

/**
 * The readouts, as a head-up display at the top centre of the canvas: they describe the picture,
 * so they sit on it. The pill lets pointer events through except on the figures themselves, which
 * carry tooltips. The connection badge appears only while the connection is not healthy; the run
 * state stays in the DOM as a live region for screen readers but is not shown, since the run
 * button, its tooltip and the moving generation counter already say it.
 */
export function CanvasHud({ status, frame }: { status: Status; frame: Frame | null }) {
  const healthy = status.variant === "success";
  return (
    <div className="viewer-hud" role="group" aria-label="Status">
      <div className="viewer-hud-pill bg-body bg-opacity-75 border rounded-pill shadow-sm px-3 py-1 small d-flex align-items-center gap-3">
        <span className={`badge text-bg-${status.variant}${healthy ? " visually-hidden" : ""}`} id="status-connection">{status.text}</span>
        <span className="visually-hidden" id="status-running" aria-live="polite">
          {frame ? (frame.running ? "running" : "paused") : "–"}
        </span>
        <Figure id="status-generation" label="Gen" tip="Generations since the initial state" minChars={5}>
          {frame ? frame.generation.toLocaleString() : "–"}
        </Figure>
        <Figure id="status-population" label="Pop" tip="Live cells in the whole universe, not only in your view" minChars={4}>
          {frame ? frame.population.toLocaleString() : "–"}
        </Figure>
        <Figure id="status-viewport" label="View" tip="Cells your view shows across × down. The zoom controls set the width; the height follows the shape of the canvas so the grid fills it.">
          {frame ? `${frame.width} × ${frame.height}` : "–"}
        </Figure>
      </div>
    </div>
  );
}
