import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";
import { Bullseye, CaretDownFill, CaretLeftFill, CaretRightFill, CaretUpFill, ZoomIn, ZoomOut } from "react-bootstrap-icons";
import { IconButton } from "./IconButton";

export interface NavigationPadProps {
  ready: boolean;
  /** Cells moved by one press, horizontally and vertically. */
  stepX: number;
  stepY: number;
  /** The one zoom variable: how many cells the view shows across. */
  across: number;
  minAcross: number;
  maxAcross: number;
  onPan(dx: number, dy: number): void;
  onZoom(factor: number): void;
  /** A typed or stepped value; the caller clamps it and derives the height. */
  onAcross(value: number): void;
  onRecentre(): void;
}

const BUTTON = "btn-outline-secondary";

/** Typing pauses this long before the value is sent; Enter and blur send it at once. */
const TYPING_DEBOUNCE_MS = 500;

/**
 * The single-pointer alternative to dragging and pinching, split the way phone games and map apps
 * split their controls: the continuous directional input (pan, with Recentre in the middle) under
 * the left thumb, the zoom stepper under the right thumb. Both float in the canvas's bottom
 * corners, the easiest reach on a phone, inset from the edge-swipe zones. Gestures remain the
 * primary way to move around.
 */
export function NavigationPad({ ready, stepX, stepY, across, minAcross, maxAcross, onPan, onZoom, onAcross, onRecentre }: NavigationPadProps) {
  const at = (column: number, row: number): CSSProperties => ({ gridColumn: column, gridRow: row });
  const pan = (dx: number, dy: number, label: string, icon: ReactNode, cell: CSSProperties) => (
    <div style={cell}>
      <IconButton label={label} tip={`${label} by ${dx !== 0 ? stepX : stepY} cells`} className={BUTTON}
                  disabled={!ready} onClick={() => onPan(dx * stepX, dy * stepY)}>{icon}</IconButton>
    </div>
  );

  // The number is the readout of the zoom buttons and accepts typed values: a stepper field.
  const [draft, setDraft] = useState<string | null>(null);
  useEffect(() => { setDraft(null); }, [across]);
  const timer = useRef<number | null>(null);
  const cancelTimer = () => { if (timer.current !== null) { window.clearTimeout(timer.current); timer.current = null; } };
  const commit = (text: string) => {
    cancelTimer();
    const value = Number(text);
    if (text.trim() !== "" && Number.isFinite(value)) onAcross(value);
    setDraft(null);
  };
  const onChange = (text: string) => {
    setDraft(text);
    cancelTimer();
    timer.current = window.setTimeout(() => commit(text), TYPING_DEBOUNCE_MS);
  };
  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") { commit(e.currentTarget.value); e.currentTarget.blur(); }
    else if (e.key === "Escape") { cancelTimer(); setDraft(null); e.currentTarget.blur(); }
    else if (e.shiftKey && (e.key === "ArrowUp" || e.key === "ArrowDown")) {
      // Shift steps by ten; the native arrows step by one.
      e.preventDefault();
      commit(String(across + (e.key === "ArrowUp" ? 10 : -10)));
    }
  };
  useEffect(() => cancelTimer, []);

  return (
    <>
      <div className="viewer-nav viewer-nav-pan" role="group" aria-label="Move the view">
        {pan(0, -1, "Pan up", <CaretUpFill aria-hidden />, at(2, 1))}
        {pan(-1, 0, "Pan left", <CaretLeftFill aria-hidden />, at(1, 2))}
        <div style={at(2, 2)}>
          <IconButton id="btn-recentre" label="Recentre on the pattern" tip="Back to the centre of the pattern" className={BUTTON}
                      disabled={!ready} onClick={onRecentre}><Bullseye aria-hidden /></IconButton>
        </div>
        {pan(1, 0, "Pan right", <CaretRightFill aria-hidden />, at(3, 2))}
        {pan(0, 1, "Pan down", <CaretDownFill aria-hidden />, at(2, 3))}
      </div>
      <div className="viewer-nav viewer-nav-zoom" role="group" aria-label="Zoom the view">
        <IconButton id="btn-zoom-out" label="Zoom out" tip="Show more cells, smaller" className={BUTTON}
                    disabled={!ready} onClick={() => onZoom(1.25)}><ZoomOut aria-hidden /></IconButton>
        <OverlayTrigger placement="top" overlay={<Tooltip id="tip-cells-across">Cells across the view ({minAcross}–{maxAcross}); fewer is closer</Tooltip>}>
          <input type="number" className="form-control form-control-sm text-center" id="cells-across" aria-label="Cells across the view"
                 min={minAcross} max={maxAcross} value={draft ?? String(across)} disabled={!ready}
                 onChange={(e) => onChange(e.target.value)} onBlur={(e) => { if (draft !== null) commit(e.target.value); }} onKeyDown={onKeyDown} />
        </OverlayTrigger>
        <IconButton id="btn-zoom-in" label="Zoom in" tip="Show fewer cells, larger" className={BUTTON}
                    disabled={!ready} onClick={() => onZoom(0.8)}><ZoomIn aria-hidden /></IconButton>
      </div>
    </>
  );
}
