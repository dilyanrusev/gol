import { useViewerConfig } from "./ViewerConfigContext";

/** Seeding stays a plain form post to the Razor page; React only renders it. */
export function SeedCard() {
  const config = useViewerConfig();
  return (
    <div className="card mb-3">
      <div className="card-header">Seed</div>
      <div className="card-body">
        <form method="post" encType="multipart/form-data" action={config.uploadUrl} className="mb-3">
          <input type="hidden" name="__RequestVerificationToken" value={config.antiforgeryToken} />
          <label htmlFor="file" className="form-label">Upload an .rle pattern</label>
          <div className="input-group">
            <input type="file" className="form-control" id="file" name="file" accept=".rle,text/plain" required />
            <button type="submit" className="btn btn-primary">Load</button>
          </div>
          <div className="form-text">Replaces the universe and pauses. Only rule B3/S23 is accepted.</div>
        </form>
        <div className="d-grid gap-2">
          <a className="btn btn-outline-primary" href={config.editorUrl}>Draw a custom seed</a>
          <a className="btn btn-outline-secondary" href={config.exportUrl}>Save current state as .rle</a>
        </div>
      </div>
    </div>
  );
}
