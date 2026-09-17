import { useEffect, useState } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import type { LifeHub } from "./useLifeHub";
import type { ViewerConfig } from "./Viewer";

export interface SimulationCardProps {
  frame: Frame | null;
  ready: boolean;
  config: ViewerConfig;
  call: LifeHub["call"];
}

/** The controls shared by every client: start, pause, step, reset, speed, and entering edit mode. */
export function SimulationCard({ frame, ready, config, call }: SimulationCardProps) {
  const running = frame?.running ?? false;
  const editing = frame?.editing ?? false;
  const mine = frame?.editingByMe ?? false;

  // The slider shows the user's value while they hold it and the server's value otherwise.
  const [speedDraft, setSpeedDraft] = useState<number | null>(null);
  const speed = speedDraft ?? frame?.generationsPerSecond ?? config.currentSpeed;
  useEffect(() => { setSpeedDraft(null); }, [frame?.generationsPerSecond]);
  const commitSpeed = () => { if (speedDraft !== null) void call("setSpeed", speedDraft); };

  const editReason = !ready ? null
    : running ? "Pause the simulation first, then you can edit cells."
    : editing && !mine ? "Another client is editing. You can edit once they are done."
    : null;

  // The wrapper carries the tooltip: a disabled button receives no pointer events.
  const editButton = (
    <div id="btn-edit-wrap" className="d-grid mb-3" tabIndex={editReason ? 0 : undefined}>
      <button type="button" className="btn btn-outline-primary" id="btn-edit"
              disabled={!ready || running || editing} onClick={() => void call("beginEdit")}>
        {mine ? "Editing…" : "Edit cells"}
      </button>
    </div>
  );

  return (
    <div className="card mb-3">
      <div className="card-header">Simulation <small className="text-body-secondary">(shared by all clients)</small></div>
      <div className="card-body">
        <div className="btn-group w-100 mb-3" role="group" aria-label="Simulation controls">
          <button type="button" className={`btn btn-success${running ? " active" : ""}`} id="btn-start"
                  disabled={!ready || running || editing} onClick={() => void call("start")}>Start</button>
          <button type="button" className={`btn btn-warning${!running ? " active" : ""}`} id="btn-pause"
                  disabled={!ready || !running} onClick={() => void call("pause")}>Pause</button>
          <button type="button" className="btn btn-outline-primary" id="btn-step"
                  disabled={!ready || editing} onClick={() => void call("step")}>Step</button>
          <button type="button" className="btn btn-outline-danger" id="btn-reset"
                  disabled={!ready || editing} onClick={() => void call("reset")}>Reset</button>
        </div>
        {editReason
          ? <OverlayTrigger placement="bottom" overlay={<Tooltip id="edit-tooltip">{editReason}</Tooltip>}>{editButton}</OverlayTrigger>
          : editButton}
        <label htmlFor="speed" className="form-label">Speed: <span id="speed-value">{speed}</span> generations/s</label>
        <input type="range" className="form-range" id="speed" min={config.minSpeed} max={config.maxSpeed} value={speed}
               disabled={!ready}
               onChange={(e) => setSpeedDraft(Number(e.target.value))}
               onPointerUp={commitSpeed} onKeyUp={commitSpeed} onBlur={commitSpeed} />
      </div>
    </div>
  );
}
