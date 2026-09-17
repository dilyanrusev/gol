import { useEffect, useState } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import { ArrowCounterclockwise, PauseFill, PencilFill, PlayFill, SkipEndFill } from "react-bootstrap-icons";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { ExplainedWhenDisabled, IconButton } from "./IconButton";
import type { LifeHub } from "./useLifeHub";
import { useViewerConfig } from "./ViewerConfigContext";

export interface SimulationControlsProps {
  frame: Frame | null;
  ready: boolean;
  call: LifeHub["call"];
  /** Why editing is unavailable right now, or null when it is available. */
  editReason: string | null;
}

/**
 * The leading toolbar groups: transport (run/pause, step, reset) with its speed, then Edit cells,
 * the in-place mode that interlocks with the transport. One button starts or pauses depending on
 * the current state; the run state is announced elsewhere, so no button ever looks pressed.
 */
export function SimulationControls({ frame, ready, call, editReason }: SimulationControlsProps) {
  const config = useViewerConfig();
  const running = frame?.running ?? false;
  const editing = frame?.editing ?? false;
  const mine = frame?.editingByMe ?? false;

  // The slider shows the user's value while they hold it and the server's value otherwise.
  const [speedDraft, setSpeedDraft] = useState<number | null>(null);
  const speed = speedDraft ?? frame?.generationsPerSecond ?? config.currentSpeed;
  useEffect(() => { setSpeedDraft(null); }, [frame?.generationsPerSecond]);
  const commitSpeed = () => { if (speedDraft !== null) void call("setSpeed", speedDraft); };

  return (
    <>
      <div className="toolbar-group" role="group" aria-label="Simulation">
        <div className="btn-group btn-group-sm" role="group" aria-label="Run and step">
          {/* Distinct keys remount the button when the state flips, so a tooltip open at the moment of
              the click does not survive into the other state. */}
          {running
            ? <IconButton key="pause" id="btn-run" label="Pause" tip="Pause the simulation for everyone" className="btn-warning"
                          disabled={!ready} onClick={() => void call("pause")}><PauseFill aria-hidden /></IconButton>
            : <IconButton key="start" id="btn-run" label="Start" tip="Run the simulation for everyone" className="btn-success"
                          disabled={!ready || editing} onClick={() => void call("start")}><PlayFill aria-hidden /></IconButton>}
          <IconButton id="btn-step" label="Step" tip="Advance one generation" className="btn-outline-primary"
                      disabled={!ready || editing} onClick={() => void call("step")}><SkipEndFill aria-hidden /></IconButton>
        </div>
        {/* Reset stands a little apart: it is the one destructive action in the group. */}
        <IconButton id="btn-reset" label="Reset" tip="Back to the initial state, generation 0" className="btn-outline-danger"
                    disabled={!ready || editing} onClick={() => void call("reset")}><ArrowCounterclockwise aria-hidden /></IconButton>
        <OverlayTrigger placement="bottom" overlay={<Tooltip id="tip-speed">Generations per second while running</Tooltip>}>
          <div className="d-flex align-items-center gap-1">
            <label htmlFor="speed" className="small text-nowrap mb-0">Speed <strong id="speed-value">{speed}</strong>/s</label>
            <input type="range" className="form-range viewer-speed" id="speed" min={config.minSpeed} max={config.maxSpeed} value={speed}
                   disabled={!ready} aria-label="Generations per second"
                   onChange={(e) => setSpeedDraft(Number(e.target.value))}
                   onPointerUp={commitSpeed} onKeyUp={commitSpeed} onBlur={commitSpeed} />
          </div>
        </OverlayTrigger>
      </div>
      <div className="vr" aria-hidden />
      <ExplainedWhenDisabled id="btn-edit-wrap" reason={editReason} tip="Flip cells by clicking them in the view">
        <button type="button" className="btn btn-sm btn-outline-primary" id="btn-edit"
                disabled={!ready || running || editing} onClick={() => void call("beginEdit")}>
          <PencilFill aria-hidden /> {mine ? "Editing…" : "Edit cells"}
        </button>
      </ExplainedWhenDisabled>
    </>
  );
}
