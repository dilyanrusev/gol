import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import OverlayTrigger from "react-bootstrap/OverlayTrigger";
import Toast from "react-bootstrap/Toast";
import ToastContainer from "react-bootstrap/ToastContainer";
import Tooltip from "react-bootstrap/Tooltip";
import { Download, QuestionCircle, Upload } from "react-bootstrap-icons";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { useStoredFlag } from "../shared/useStoredFlag";
import { CanvasHud } from "./CanvasHud";
import { CanvasOverlay } from "./CanvasOverlay";
import { EditBanner } from "./EditBanner";
import { GESTURE_HINT_KEY, GestureHint } from "./GestureHint";
import { HelpOffcanvas } from "./HelpOffcanvas";
import { ExplainedWhenDisabled, IconButton } from "./IconButton";
import { NavigationPad } from "./NavigationPad";
import { SeedOffcanvas } from "./SeedOffcanvas";
import { SimulationControls } from "./SimulationControls";
import { ThemeButton } from "./ThemeButton";
import { ToolbarStrip } from "./ToolbarStrip";
import { UniverseCanvas } from "./UniverseCanvas";
import { NOTICE_MS, useLifeHub, type LifeHub } from "./useLifeHub";
import { useViewerConfig } from "./ViewerConfigContext";

/** Cells smaller than this (CSS px) cannot be targeted reliably, so clicks are refused until the user zooms in. */
const MIN_EDIT_CELL_PX = 4;

/** A visible pattern spanning at most this share of the viewport's side is called tiny. */
const TINY_PATTERN_SHARE = 0.05;

/** How long the canvas may keep changing size (a window being dragged) before the view follows. */
const FOLLOW_CANVAS_MS = 250;

/** Viewport changes are coalesced: at most one pan in flight, the rest accumulate. */
function usePanQueue(hub: LifeHub) {
  const pending = useRef({ dx: 0, dy: 0, inFlight: false });
  const { call, notify } = hub;
  return useCallback((dx: number, dy: number) => {
    const p = pending.current;
    p.dx += dx;
    p.dy += dy;
    if (p.inFlight) return;
    p.inFlight = true;
    void (async () => {
      try {
        while (p.dx !== 0 || p.dy !== 0) {
          const [x, y] = [p.dx, p.dy];
          p.dx = p.dy = 0;
          if (!Number.isSafeInteger(x) || !Number.isSafeInteger(y)) { notify("That pan is too large."); break; }
          await call("pan", x, y);
        }
      } finally {
        p.inFlight = false;
      }
    })();
  }, [call, notify]);
}

/** Bounding box of the visible cells, in viewport cells. */
function visibleBounds(frame: Frame) {
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const i of frame.cells) {
    const x = i % frame.width;
    const y = (i - x) / frame.width;
    if (x < minX) minX = x;
    if (x > maxX) maxX = x;
    if (y < minY) minY = y;
    if (y > maxY) maxY = y;
  }
  return { minX, minY, maxX, maxY, spanX: maxX - minX + 1, spanY: maxY - minY + 1 };
}

/**
 * The universe page: one thin strip of controls above the canvas, and a navigation pad floating in
 * the canvas's corner. Everything that is not needed under normal conditions lives behind a
 * toolbar button.
 */
export function Viewer() {
  const config = useViewerConfig();
  const hub = useLifeHub();
  const { frame, ready, status, notice, notify, dismissNotice, call } = hub;

  // Until the server has spoken the canvas shows an empty default-sized grid; nothing trusts it as state.
  const view: Frame = frame ?? {
    generation: 0, population: 0, running: false, generationsPerSecond: config.currentSpeed,
    width: config.defaultGridSize, height: config.defaultGridSize, cells: [],
    editing: false, editingByMe: false, editRemainingMs: 0, editTimeoutMs: 0,
  };

  const pan = usePanQueue(hub);
  const recentre = useCallback(() => call("recentre"), [call]);

  // One zoom variable: cells across the view. The height follows the canvas's shape, so the grid
  // fills it; the server still stores width and height, and clamps them as well.
  const [canvasSize, setCanvasSize] = useState({ width: 0, height: 0 });
  const onCanvasSize = useCallback((width: number, height: number) => setCanvasSize({ width, height }), []);
  const clampAcross = useCallback((value: number) =>
    Math.min(config.maxGridSize, Math.max(config.minGridSize, Math.round(value))), [config.maxGridSize, config.minGridSize]);
  const heightFor = useCallback((across: number) => {
    const aspect = canvasSize.width > 0 ? canvasSize.height / canvasSize.width : 1;
    return clampAcross(across * aspect);
  }, [canvasSize, clampAcross]);
  const setAcross = useCallback((requested: number) => {
    const across = clampAcross(requested);
    if (across !== Math.round(requested))
      notify(`The view is limited to ${config.minGridSize}–${config.maxGridSize} cells across; using ${across}.`, "warning");
    return call("resize", across, heightFor(across));
  }, [clampAcross, heightFor, call, notify, config.minGridSize, config.maxGridSize]);
  const zoom = useCallback((factor: number) => setAcross(view.width * factor), [setAcross, view.width]);

  // When the canvas changes shape (window resized, hint dismissed), re-request the same width with
  // the matching height, once the size has settled.
  useEffect(() => {
    if (!ready || !frame || canvasSize.width === 0) return;
    const wanted = heightFor(frame.width);
    if (wanted === frame.height) return;
    const timer = window.setTimeout(() => { void call("resize", frame.width, wanted); }, FOLLOW_CANVAS_MS);
    return () => window.clearTimeout(timer);
  }, [ready, frame, canvasSize, heightFor, call]);
  const editingByMe = frame?.editingByMe ?? false;
  const tapCell = useCallback((x: number, y: number, cellPx: number) => {
    if (!editingByMe) return;
    if (cellPx < MIN_EDIT_CELL_PX) { notify("Zoom in to edit: the cells are too small to click.", "warning"); return; }
    void call("toggleCell", x, y);
  }, [editingByMe, call, notify]);

  // A pad press moves by a tenth of the view, at least one cell.
  const stepX = Math.max(1, Math.round(view.width / 10));
  const stepY = Math.max(1, Math.round(view.height / 10));

  // Availability follows the shared state; each unavailable control explains why.
  const running = frame?.running ?? false;
  const editing = frame?.editing ?? false;
  const seedReason = !ready ? null
    : running ? "Pause the simulation to load a pattern."
    : editing ? "Wait until editing is finished to load a pattern."
    : null;
  const canSeed = ready && seedReason === null;
  const exportReason = !ready ? null : editing ? "Available once editing is finished." : null;
  const canExport = ready && exportReason === null;
  const editReason = !ready ? null
    : running ? "Pause the simulation first, then you can edit cells."
    : editing && !editingByMe ? "Another client is editing. You can edit once they are done."
    : null;

  // The pattern panel opens and closes on the user's intent only.
  const [seedOpen, setSeedOpen] = useState(false);
  // The one-time tip is a per-browser convenience: dismissed once, gone until asked for again.
  const [hintDismissed, setHintDismissed] = useStoredFlag(GESTURE_HINT_KEY);
  const [helpOpen, setHelpOpen] = useState(false);

  // The trailing group: everything that leaves the page or opens a panel.
  const exportLink = (
    <a className={`btn btn-sm btn-outline-secondary${canExport ? "" : " disabled"}`} id="btn-export" href={config.exportUrl}
       aria-disabled={!canExport}>
      <Download aria-hidden /> Save .rle
    </a>
  );
  const trailingTools = (
    <>
      <div className="toolbar-group ms-auto" role="group" aria-label="Patterns">
        <ExplainedWhenDisabled id="btn-seed-wrap" reason={seedReason} tip="Upload an .rle file or draw a pattern">
          <button type="button" className="btn btn-sm btn-outline-primary" id="btn-seed" disabled={!canSeed} onClick={() => setSeedOpen(true)}>
            <Upload aria-hidden /> Load pattern…
          </button>
        </ExplainedWhenDisabled>
        <ExplainedWhenDisabled id="btn-export-wrap" reason={exportReason}>
          {canExport
            ? <OverlayTrigger placement="bottom" overlay={<Tooltip id="tip-export">Save the current state as an .rle file</Tooltip>}>{exportLink}</OverlayTrigger>
            : exportLink}
        </ExplainedWhenDisabled>
      </div>
      <div className="vr" aria-hidden />
      <ThemeButton />
      <IconButton id="btn-help" label="Help" tip="Gestures, keyboard, editing and patterns" onClick={() => setHelpOpen(true)}>
        <QuestionCircle aria-hidden />
      </IconButton>
    </>
  );

  // What an empty-looking canvas cannot say for itself.
  const bounds = useMemo(() => (frame && frame.cells.length > 0 ? visibleBounds(frame) : null), [frame]);
  const tiny = bounds !== null && frame !== null
    && bounds.spanX <= frame.width * TINY_PATTERN_SHARE && bounds.spanY <= frame.height * TINY_PATTERN_SHARE
    && Math.min(frame.width, frame.height) > config.minGridSize * 2;
  const zoomToFit = useCallback(async () => {
    if (!frame || !bounds) return;
    // Centre the viewport on the pattern first, so shrinking around the centre keeps it in view.
    const dx = Math.round((bounds.minX + bounds.maxX) / 2 - frame.width / 2);
    const dy = Math.round((bounds.minY + bounds.maxY) / 2 - frame.height / 2);
    if (dx !== 0 || dy !== 0) await call("pan", dx, dy);
    // Enough cells across that the pattern fills a quarter of the view in its larger dimension.
    const aspect = canvasSize.width > 0 ? canvasSize.height / canvasSize.width : 1;
    await setAcross(Math.max(bounds.spanX * 4, bounds.spanY * 4 / aspect));
  }, [frame, bounds, call, setAcross, canvasSize]);

  // What an empty-looking canvas cannot say for itself, and the mode instruction while editing.
  let overlay: ReactNode = null;
  if (ready && frame && editingByMe) {
    overlay = (
      <CanvasOverlay id="canvas-editing">
        <span><strong>Editing.</strong> Click a cell to flip it. Drag still pans, scroll still zooms.</span>
      </CanvasOverlay>
    );
  } else if (ready && frame) {
    if (frame.population === 0 && !running) {
      overlay = (
        <CanvasOverlay id="canvas-empty">
          <span>The universe is empty.</span>
          <button type="button" className="btn btn-sm btn-primary" id="btn-empty-seed" disabled={!canSeed} onClick={() => setSeedOpen(true)}>Load a pattern</button>
          <button type="button" className="btn btn-sm btn-outline-primary" id="btn-empty-edit" disabled={editReason !== null} onClick={() => void call("beginEdit")}>Draw cells</button>
        </CanvasOverlay>
      );
    } else if (frame.population > 0 && frame.cells.length === 0) {
      overlay = (
        <CanvasOverlay id="canvas-offscreen">
          <span>The pattern is outside your view.</span>
          <button type="button" className="btn btn-sm btn-primary" id="btn-overlay-recentre" onClick={recentre}>Recentre</button>
        </CanvasOverlay>
      );
    } else if (tiny) {
      overlay = (
        <CanvasOverlay id="canvas-tiny">
          <span>The pattern is tiny at this zoom.</span>
          <button type="button" className="btn btn-sm btn-primary" id="btn-zoom-fit" onClick={() => void zoomToFit()}>Zoom in</button>
        </CanvasOverlay>
      );
    }
  }

  return (
    <>
      <EditBanner frame={frame} ready={ready} onDone={() => void call("endEdit")} onCancel={() => void call("cancelEdit")} />
      <div className="viewer-main">
        <ToolbarStrip id="toolbar-top" className="mb-2">
          <SimulationControls frame={frame} ready={ready} call={call} editReason={editReason} />
          {trailingTools}
        </ToolbarStrip>
        <div className="viewer-canvas-wrap">
          <UniverseCanvas frame={view} editing={editingByMe} onPan={pan} onZoom={zoom} onRecentre={recentre} onCellTap={tapCell} onSizeChange={onCanvasSize} />
          <CanvasHud status={status} frame={ready ? frame : null} />
          {overlay}
          <NavigationPad ready={ready} stepX={stepX} stepY={stepY} across={view.width} minAcross={config.minGridSize} maxAcross={config.maxGridSize}
                         onPan={pan} onZoom={zoom} onAcross={(v) => void setAcross(v)} onRecentre={recentre} />
          {/* Transient messages float over the canvas, bottom centre, between the navigation clusters. */}
          <ToastContainer position="bottom-center" containerPosition="absolute" className="p-2 viewer-toasts">
            {notice && (
              <Toast key={notice.key} id="notice" bg={notice.variant} autohide delay={NOTICE_MS} onClose={dismissNotice}>
                <Toast.Body className={notice.variant === "danger" ? "text-white" : undefined}>{notice.text}</Toast.Body>
              </Toast>
            )}
          </ToastContainer>
        </div>
        {!hintDismissed && <div className="mt-2"><GestureHint onDismiss={() => setHintDismissed(true)} /></div>}
      </div>
      <SeedOffcanvas show={seedOpen} onHide={() => setSeedOpen(false)} canSeed={canSeed} reason={seedReason} />
      <HelpOffcanvas show={helpOpen} onHide={() => setHelpOpen(false)} tipDismissed={hintDismissed}
                     onShowTip={() => { setHintDismissed(false); setHelpOpen(false); }} />
    </>
  );
}
