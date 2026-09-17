import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import type { Status } from "./useLifeHub";

export function StatusBar({ status, frame }: { status: Status; frame: Frame | null }) {
  return (
    <div className="d-flex flex-wrap align-items-center gap-3 mb-2">
      <span className={`badge text-bg-${status.variant}`} id="status-connection">{status.text}</span>
      <span className={`badge ${frame?.running ? "text-bg-success" : "text-bg-secondary"}`} id="status-running" aria-live="polite">
        {frame ? (frame.running ? "running" : "paused") : "–"}
      </span>
      <span>Generation <strong id="status-generation">{frame ? frame.generation.toLocaleString() : "–"}</strong></span>
      <span>Population <strong id="status-population">{frame ? frame.population.toLocaleString() : "–"}</strong></span>
      <span>Viewport <strong id="status-viewport">{frame ? `${frame.width} × ${frame.height}` : "–"}</strong> cells</span>
    </div>
  );
}
