import Offcanvas from "react-bootstrap/Offcanvas";
import { useViewerConfig } from "./ViewerConfigContext";

export interface SeedOffcanvasProps {
  show: boolean;
  onHide(): void;
  /** Whether a seed may be loaded right now (paused, nobody editing). */
  canSeed: boolean;
  /** Why not, when it cannot; shown inside the panel so it never has to close on its own. */
  reason: string | null;
}

/**
 * The pattern-loading tools, hidden until asked for. Opening and closing follow the user's intent;
 * only the form's availability follows the shared state, so another client starting the simulation
 * cannot pull the panel away mid-upload. "Pattern" is what goes in; "initial state" is what Reset
 * returns to.
 */
export function SeedOffcanvas({ show, onHide, canSeed, reason }: SeedOffcanvasProps) {
  const config = useViewerConfig();
  return (
    <Offcanvas show={show} onHide={onHide} placement="end" id="seed-panel" aria-labelledby="seed-panel-title">
      <Offcanvas.Header closeButton>
        <Offcanvas.Title id="seed-panel-title">Load a pattern</Offcanvas.Title>
      </Offcanvas.Header>
      <Offcanvas.Body className="d-flex flex-column gap-3">
        {!canSeed && reason && <div className="alert alert-warning small mb-0" id="seed-blocked" role="status">{reason}</div>}
        {/* Seeding stays a plain form post to the Razor page; React only renders it. */}
        <form method="post" encType="multipart/form-data" action={config.uploadUrl}>
          <input type="hidden" name="__RequestVerificationToken" value={config.antiforgeryToken} />
          <fieldset disabled={!canSeed}>
            <label htmlFor="file" className="form-label">Upload an .rle file</label>
            <div className="input-group">
              <input type="file" className="form-control" id="file" name="file" accept=".rle,text/plain" required />
              <button type="submit" className="btn btn-primary" id="btn-upload">Load</button>
            </div>
            <div className="form-text">Only rule B3/S23 is accepted.</div>
          </fieldset>
        </form>
        <a className="btn btn-outline-primary" href={config.editorUrl} id="link-editor">Draw a pattern</a>
        <p className="form-text mb-0">Loading replaces the universe for everyone. Edits made at generation 0 with Edit cells become the initial state too.</p>
      </Offcanvas.Body>
    </Offcanvas>
  );
}
