/** Storage key for the dismissed state; bump the version to show a reworded hint once more. */
export const GESTURE_HINT_KEY = "gol.hint.gestures.v2";

/**
 * The one-time orientation: how to move, and that drawing exists. Dismissible; the Help panel can
 * bring it back. Laid out with flex rather than Bootstrap's alert-dismissible, whose absolutely
 * positioned close button is taller than this compact alert.
 */
export function GestureHint({ onDismiss }: { onDismiss(): void }) {
  return (
    <div className="alert alert-info d-flex align-items-center gap-2 py-2 mb-2 small" role="note" id="gesture-hint">
      <span className="flex-grow-1">
        Drag to pan, scroll or pinch to zoom. Pause and press Edit cells to draw your own pattern. Everything else is under Help.
      </span>
      <button type="button" className="btn-close flex-shrink-0" id="gesture-hint-close" aria-label="Dismiss this hint" onClick={onDismiss} />
    </div>
  );
}
