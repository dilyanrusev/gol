import { useCallback, useRef } from "react";
import type { Frame } from "../generated/GameOfLife.Web.Simulation";
import { EditBanner } from "./EditBanner";
import { SeedCard } from "./SeedCard";
import { SimulationCard } from "./SimulationCard";
import { StatusBar } from "./StatusBar";
import { UniverseCanvas } from "./UniverseCanvas";
import { ViewportCard } from "./ViewportCard";
import { useLifeHub, type LifeHub } from "./useLifeHub";
import { useViewerConfig } from "./ViewerConfigContext";

/** Cells smaller than this (CSS px) cannot be targeted reliably, so clicks are refused until the user zooms in. */
const MIN_EDIT_CELL_PX = 4;

/** Viewport changes are coalesced: at most one pan in flight, the rest accumulate. */
function usePanQueue(hub: LifeHub) {
  const pending = useRef({ dx: 0, dy: 0, inFlight: false });
  const { call, setStatus } = hub;
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
          if (!Number.isSafeInteger(x) || !Number.isSafeInteger(y)) { setStatus({ text: "pan too large", variant: "danger" }); break; }
          await call("pan", x, y);
        }
      } finally {
        p.inFlight = false;
      }
    })();
  }, [call, setStatus]);
}

function editHint(frame: Frame | null): string {
  if (!frame) return "Click Edit cells, then click cells in the view to flip them.";
  if (frame.running) return "Pause the simulation to edit cells.";
  if (frame.editing && !frame.editingByMe) return "Another client is editing; you can edit once they are done.";
  if (frame.editingByMe) return "Click or tap a cell to flip it. Drag still pans, scroll or pinch still zooms.";
  return "Click Edit cells, then click cells in the view to flip them.";
}

export function Viewer() {
  const config = useViewerConfig();
  const hub = useLifeHub();
  const { frame, ready, status, setStatus, call } = hub;

  // Until the server has spoken the canvas shows an empty default-sized grid; nothing trusts it as state.
  const view: Frame = frame ?? {
    generation: 0, population: 0, running: false, generationsPerSecond: config.currentSpeed,
    width: config.defaultGridSize, height: config.defaultGridSize, cells: [],
    editing: false, editingByMe: false, editRemainingMs: 0, editTimeoutMs: 0,
  };

  const pan = usePanQueue(hub);
  const resize = useCallback((w: number, h: number) => call("resize", Math.round(w), Math.round(h)), [call]);
  const zoom = useCallback((factor: number) => resize(view.width * factor, view.height * factor), [resize, view.width, view.height]);
  const recentre = useCallback(() => call("recentre"), [call]);
  const editingByMe = frame?.editingByMe ?? false;
  const tapCell = useCallback((x: number, y: number, cellPx: number) => {
    if (!editingByMe) return;
    if (cellPx < MIN_EDIT_CELL_PX) { setStatus({ text: "zoom in to edit: cells are too small to click", variant: "warning" }); return; }
    void call("toggleCell", x, y);
  }, [editingByMe, call, setStatus]);

  return (
    <>
      <EditBanner frame={frame} ready={ready} onDone={() => void call("endEdit")} onCancel={() => void call("cancelEdit")} />
      <div className="row g-3">
        <div className="col-lg-8">
          <StatusBar status={status} frame={ready ? frame : null} />
          <UniverseCanvas frame={view} editing={editingByMe} onPan={pan} onZoom={zoom} onRecentre={recentre} onCellTap={tapCell} />
          <p className="form-text">Drag to pan, scroll or pinch to zoom. Keyboard: arrows pan, + and - zoom, Home recentres.</p>
          <p className="form-text" id="edit-hint">{editHint(ready ? frame : null)}</p>
        </div>
        <div className="col-lg-4">
          <SimulationCard frame={frame} ready={ready} call={call} />
          <ViewportCard frame={frame} ready={ready} onPan={pan} onZoom={zoom} onRecentre={recentre} onResize={resize} />
          <SeedCard />
        </div>
      </div>
    </>
  );
}
