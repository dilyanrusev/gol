import { useState, type ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Tooltip from "react-bootstrap/Tooltip";

export interface IconButtonProps {
  id?: string;
  /** Accessible name; the icon carries none. */
  label: string;
  /** Tooltip text. Only shown while the button is enabled: a disabled button gets no pointer events. */
  tip: string;
  className?: string;
  disabled?: boolean;
  onClick(): void;
  children: ReactNode;
}

/**
 * A small toolbar button with a tooltip. The tooltip is keyed on its text, so a button whose text
 * changes when clicked (a mode switch) shows the new text at once while the pointer stays on it,
 * instead of the stale one.
 */
export function IconButton({ id, label, tip, className = "btn-outline-secondary", disabled = false, onClick, children }: IconButtonProps) {
  const key = id ?? label.replace(/\s+/g, "-").toLowerCase();
  const [show, setShow] = useState(false);
  return (
    <OverlayTrigger placement="top" show={show} onToggle={setShow} overlay={<Tooltip id={`tip-${key}`} key={tip}>{tip}</Tooltip>}>
      <button type="button" className={`btn btn-sm ${className}`} id={id} aria-label={label} disabled={disabled} onClick={onClick}>
        {children}
      </button>
    </OverlayTrigger>
  );
}

export interface ExplainedProps {
  id: string;
  /** Why the control is unavailable, or null when it is available. */
  reason: string | null;
  /** What the control does, shown while it is available. */
  tip?: string;
  children: ReactNode;
}

/**
 * Wraps a control so that it always has something to say: its reason while disabled, its purpose
 * while enabled. Disabled elements receive no pointer events, so the tooltip lives on a focusable
 * wrapper in that case.
 */
export function ExplainedWhenDisabled({ id, reason, tip, children }: ExplainedProps) {
  if (reason) {
    return (
      <OverlayTrigger placement="top" overlay={<Tooltip id={`tip-${id}`}>{reason}</Tooltip>}>
        <div id={id} className="d-inline-block" tabIndex={0}>{children}</div>
      </OverlayTrigger>
    );
  }
  const wrapper = <div id={id} className="d-inline-block">{children}</div>;
  return tip
    ? <OverlayTrigger placement="top" overlay={<Tooltip id={`tip-${id}`}>{tip}</Tooltip>}>{wrapper}</OverlayTrigger>
    : wrapper;
}
