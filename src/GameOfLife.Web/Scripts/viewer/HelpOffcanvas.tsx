import Offcanvas from "react-bootstrap/Offcanvas";

export interface HelpOffcanvasProps {
  show: boolean;
  onHide(): void;
  /** Whether the one-time gesture tip has been dismissed, so it can be offered back. */
  tipDismissed: boolean;
  onShowTip(): void;
}

/** The reference: everything the one-time tip and the tooltips say, in one place that is always reachable. */
export function HelpOffcanvas({ show, onHide, tipDismissed, onShowTip }: HelpOffcanvasProps) {
  return (
    <Offcanvas show={show} onHide={onHide} placement="end" id="help-panel" aria-labelledby="help-panel-title">
      <Offcanvas.Header closeButton>
        <Offcanvas.Title id="help-panel-title">Help</Offcanvas.Title>
      </Offcanvas.Header>
      <Offcanvas.Body className="small">
        <h2 className="h6">Moving around</h2>
        <ul>
          <li>Drag the view to pan; scroll or pinch to zoom.</li>
          <li>The pad in the bottom-left corner pans and recentres; the buttons bottom-right zoom.</li>
          <li>Grid sets how many cells the view shows per side.</li>
        </ul>
        <h2 className="h6">Keyboard</h2>
        <p className="mb-1">With the view focused (click it or tab to it):</p>
        <ul>
          <li>Arrow keys pan by one cell, ten with Shift.</li>
          <li><kbd>+</kbd> and <kbd>-</kbd> zoom; <kbd>Home</kbd> recentres.</li>
        </ul>
        <h2 className="h6">Editing cells</h2>
        <ul>
          <li>Pause, press <strong>Edit cells</strong>, then click cells in the view to flip them. Zoom in if they are too small to hit.</li>
          <li>Only one client can edit at a time; Start, Step and Reset wait until they finish.</li>
          <li><strong>Done</strong> keeps the edits and resumes; <strong>Cancel</strong> discards them. An idle session ends on its own.</li>
          <li>Edits made at generation 0 become the initial state, so Reset returns to them.</li>
        </ul>
        <h2 className="h6">Patterns</h2>
        <ul>
          <li><strong>Load pattern…</strong> uploads an .rle file or takes you to the pattern editor. Loading replaces the universe for everyone.</li>
          <li>The download button saves the current state as an .rle file.</li>
        </ul>
        {tipDismissed && (
          <button type="button" className="btn btn-outline-secondary btn-sm" id="btn-show-tip" onClick={onShowTip}>Show the quick tip again</button>
        )}
      </Offcanvas.Body>
    </Offcanvas>
  );
}
