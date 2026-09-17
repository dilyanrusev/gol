import { useEffect, useMemo, useState } from "react";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";

export interface EditBannerProps {
  frame: Frame | null;
  ready: boolean;
  onDone(): void;
  onCancel(): void;
}

/**
 * Shown to everyone while a client holds the edit session: who is editing, what that blocks, and
 * a countdown with a bar that starts full and empties. Bootstrap colours follow the share of the
 * timeout left: success above half, warning down to a fifth, danger below that.
 */
export function EditBanner({ frame, ready, onDone, onCancel }: EditBannerProps) {
  const editing = ready && frame !== null && frame.editing;
  // Frames carry the remaining time, so each one re-anchors the deadline on this page's clock;
  // between frames the countdown runs locally.
  const deadline = useMemo(() => performance.now() + (frame?.editRemainingMs ?? 0), [frame]);
  const [now, setNow] = useState(() => performance.now());

  useEffect(() => {
    if (!editing) return;
    setNow(performance.now());
    const timer = window.setInterval(() => setNow(performance.now()), 250);
    return () => window.clearInterval(timer);
  }, [editing]);

  if (!editing || frame === null) return null;

  const remainingMs = Math.max(0, deadline - now);
  const fraction = frame.editTimeoutMs > 0 ? Math.min(1, remainingMs / frame.editTimeoutMs) : 0;
  const totalSeconds = Math.ceil(remainingMs / 1000);
  const countdown = `${Math.floor(totalSeconds / 60)}:${String(totalSeconds % 60).padStart(2, "0")}`;
  const level = fraction > 0.5 ? "success" : fraction > 0.2 ? "warning" : "danger";
  const mine = frame.editingByMe;

  return (
    <div id="edit-banner" className={`alert alert-${level} sticky-top shadow mb-3`} role="status" aria-live="polite">
      <div className="d-flex flex-wrap align-items-center gap-3">
        <div className="flex-grow-1">
          <strong id="edit-banner-title">{mine ? "You are editing the universe." : "Another client is editing the universe."}</strong>{" "}
          <span id="edit-banner-text">
            {mine
              ? "Nobody can start, step or reset until you finish. Done keeps your edits and resumes the simulation; Cancel discards them and stays paused. Every click restarts the timer; when it runs out your edits are kept and the simulation stays paused."
              : "Start, Step and Reset are disabled for everyone until they finish or the timer runs out. Each of their edits restarts the timer."}
          </span>
        </div>
        <div className="text-nowrap">Unlocks in <strong id="edit-countdown" className="font-monospace">{countdown}</strong></div>
        {mine && (
          <div className="btn-group" id="edit-banner-actions" role="group" aria-label="Finish editing">
            <button type="button" className="btn btn-primary" id="btn-edit-done" onClick={onDone}>Done</button>
            <button type="button" className="btn btn-outline-secondary" id="btn-edit-cancel" onClick={onCancel}>Cancel</button>
          </div>
        )}
      </div>
      <div className="progress mt-2" id="edit-progress" role="progressbar" aria-label="Time left before editing unlocks"
           aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(fraction * 100)}>
        <div className={`progress-bar bg-${level}`} id="edit-progress-bar" style={{ width: `${fraction * 100}%` }} />
      </div>
    </div>
  );
}
