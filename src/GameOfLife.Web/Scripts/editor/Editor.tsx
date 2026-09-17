import { memo, useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent, type PointerEvent } from "react";
import { PRESETS } from "./presets";
import { decodeRle, encodeRle } from "./rle";

export interface EditorConfig {
  size: number;
  initialRle: string;
  initialName: string;
  /** Where the form posts (the Razor page itself). */
  action: string;
  indexUrl: string;
  antiforgeryToken: string;
}

/** The grid as immutable rows, so painting a cell re-renders only its row. */
type Rows = ReadonlyArray<ReadonlyArray<boolean>>;

const emptyRows = (size: number): Rows => Array.from({ length: size }, () => Array<boolean>(size).fill(false));

function withCell(rows: Rows, x: number, y: number, on: boolean): Rows {
  if (rows[y][x] === on) return rows;
  const row = rows[y].slice();
  row[x] = on;
  const next = rows.slice();
  next[y] = row;
  return next;
}

function liveCells(rows: Rows): Array<[number, number]> {
  const cells: Array<[number, number]> = [];
  rows.forEach((row, y) => row.forEach((on, x) => { if (on) cells.push([x, y]); }));
  return cells;
}

/** Places a decoded pattern centred on an empty grid; throws if it does not fit. */
function rowsFromRle(text: string, size: number): Rows {
  const pattern = decodeRle(text);
  if (pattern.width > size || pattern.height > size)
    throw new Error(`The pattern is ${pattern.width} x ${pattern.height}; it must fit in ${size} x ${size}.`);
  const rows = emptyRows(size).map((row) => row.slice());
  const ox = Math.floor((size - pattern.width) / 2);
  const oy = Math.floor((size - pattern.height) / 2);
  for (const [x, y] of pattern.cells) rows[y + oy][x + ox] = true;
  return rows;
}

const cellIndex = (target: EventTarget | null): number | null => {
  const i = (target as HTMLElement | null)?.dataset?.i;
  return i === undefined ? null : Number(i);
};

/** One row of cells. Memoised so a paint stroke only re-renders the rows it touches. */
const Row = memo(function Row({ y, cells, focusX }: { y: number; cells: ReadonlyArray<boolean>; focusX: number }) {
  return (
    <div role="row" style={{ display: "contents" }}>
      {cells.map((on, x) => (
        <button key={x} type="button" className="cell" role="gridcell" aria-pressed={on} aria-label={`cell ${x + 1}, ${y + 1}`}
                tabIndex={x === focusX ? 0 : -1} data-i={y * cells.length + x} />
      ))}
    </div>
  );
});

/**
 * The pattern editor: a size x size matrix of real buttons (clickable, keyboard-navigable with a roving
 * tabindex) kept in sync with an RLE textarea that is posted back to the Razor page.
 */
export function Editor({ config }: { config: EditorConfig }) {
  const { size } = config;
  const initial = useMemo(() => {
    const text = config.initialRle.trim();
    if (!text) return { rows: emptyRows(size), rle: null as string | null, error: null as string | null };
    try {
      return { rows: rowsFromRle(text, size), rle: null, error: null };
    } catch (err) {
      // Keep the user's text so they can fix it; the grid starts empty.
      return { rows: emptyRows(size), rle: config.initialRle, error: (err as Error).message };
    }
  }, [config.initialRle, size]);

  const [rows, setRows] = useState<Rows>(initial.rows);
  const [name, setName] = useState(config.initialName);
  const [rleText, setRleText] = useState(() => initial.rle ?? encodeRle(liveCells(initial.rows), config.initialName || undefined));
  const [error, setError] = useState<string | null>(initial.error);
  const [focus, setFocus] = useState({ x: 0, y: 0 });

  const rowsRef = useRef(rows);
  useEffect(() => { rowsRef.current = rows; }, [rows]);
  const liveCount = useMemo(() => rows.reduce((n, row) => n + row.reduce((m, on) => m + (on ? 1 : 0), 0), 0), [rows]);

  /** Replaces the grid and re-encodes the textarea from it. */
  const commitRows = useCallback((next: Rows, nextName = name) => {
    setRows(next);
    setRleText(encodeRle(liveCells(next), nextName || undefined));
  }, [name]);

  const loadRle = (text: string) => {
    try {
      commitRows(rowsFromRle(text, size));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  // --- mouse / touch painting: press toggles the first cell, dragging paints the same state ---
  const paintState = useRef<boolean | null>(null);
  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    const i = cellIndex(e.target);
    if (i === null) return;
    const x = i % size;
    const y = Math.floor(i / size);
    paintState.current = !rowsRef.current[y][x];
    setRows((r) => withCell(r, x, y, paintState.current!));
    setFocus({ x, y });
    (e.target as HTMLButtonElement).focus({ preventScroll: true });
    e.preventDefault();
  };
  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (paintState.current === null) return;
    // Touch events keep targeting the first element pressed, so look up what is under the pointer.
    const i = cellIndex(document.elementFromPoint(e.clientX, e.clientY));
    if (i !== null) setRows((r) => withCell(r, i % size, Math.floor(i / size), paintState.current!));
  };
  useEffect(() => {
    const stop = () => {
      if (paintState.current === null) return;
      paintState.current = null;
      commitRows(rowsRef.current);
    };
    window.addEventListener("pointerup", stop);
    window.addEventListener("pointercancel", stop);
    return () => {
      window.removeEventListener("pointerup", stop);
      window.removeEventListener("pointercancel", stop);
    };
  }, [commitRows]);

  // --- keyboard: arrows move focus (roving tabindex), Space/Enter toggle ---
  const gridRef = useRef<HTMLDivElement>(null);
  const pendingFocus = useRef(false);
  useEffect(() => {
    if (!pendingFocus.current) return;
    pendingFocus.current = false;
    gridRef.current?.querySelector<HTMLButtonElement>(`[data-i="${focus.y * size + focus.x}"]`)?.focus({ preventScroll: true });
  }, [focus, size]);

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const i = cellIndex(e.target);
    if (i === null) return;
    const x = i % size;
    const y = Math.floor(i / size);
    let nx = x;
    let ny = y;
    switch (e.key) {
      case "ArrowLeft": nx = Math.max(0, x - 1); break;
      case "ArrowRight": nx = Math.min(size - 1, x + 1); break;
      case "ArrowUp": ny = Math.max(0, y - 1); break;
      case "ArrowDown": ny = Math.min(size - 1, y + 1); break;
      case "Home": nx = 0; break;
      case "End": nx = size - 1; break;
      case "PageUp": ny = 0; break;
      case "PageDown": ny = size - 1; break;
      case " ":
      case "Enter":
        commitRows(withCell(rowsRef.current, x, y, !rowsRef.current[y][x]));
        e.preventDefault();
        return;
      default: return;
    }
    e.preventDefault();
    pendingFocus.current = true;
    setFocus({ x: nx, y: ny });
  };

  const onNameChange = (value: string) => {
    setName(value);
    setRleText(encodeRle(liveCells(rowsRef.current), value || undefined));
  };

  return (
    <div className="row g-3">
      <div className="col-lg-8">
        <div className="d-flex flex-wrap gap-2 mb-2">
          <button type="button" className="btn btn-outline-danger btn-sm" id="btn-clear" onClick={() => commitRows(emptyRows(size))}>Clear</button>
          <div className="btn-group btn-group-sm" role="group" aria-label="Presets">
            {PRESETS.map((p) => (
              <button key={p.key} type="button" className="btn btn-outline-secondary" data-preset={p.key} onClick={() => loadRle(p.rle)}>{p.label}</button>
            ))}
          </div>
          <span className="align-self-center ms-auto">Live cells: <strong id="editor-count">{liveCount}</strong></span>
        </div>
        {error && <div className="alert alert-danger" role="alert" id="editor-error">{error}</div>}
        <div id="editor-grid" ref={gridRef} className="editor-grid border rounded" role="grid" aria-label="Pattern"
             style={{ "--editor-size": size } as CSSProperties}
             onPointerDown={onPointerDown} onPointerMove={onPointerMove} onKeyDown={onKeyDown}>
          {rows.map((cells, y) => <Row key={y} y={y} cells={cells} focusX={focus.y === y ? focus.x : -1} />)}
        </div>
      </div>

      <div className="col-lg-4">
        <form method="post" action={config.action}>
          <input type="hidden" name="__RequestVerificationToken" value={config.antiforgeryToken} />
          <div className="mb-3">
            <label htmlFor="Name" className="form-label">Pattern name (optional)</label>
            <input id="Name" name="Name" className="form-control" maxLength={100} value={name} onChange={(e) => onNameChange(e.target.value)} />
          </div>
          <div className="mb-3">
            <label htmlFor="editor-rle" className="form-label">RLE</label>
            <textarea id="editor-rle" name="Rle" className="form-control font-monospace" rows={12} spellCheck={false}
                      value={rleText} onChange={(e) => setRleText(e.target.value)} />
            <div className="form-text">Kept in sync with the grid. You can also paste a pattern here and press Apply.</div>
          </div>
          <div className="d-grid gap-2">
            <button type="button" className="btn btn-outline-secondary" id="btn-apply-rle" onClick={() => loadRle(rleText)}>Apply RLE to grid</button>
            <button type="submit" className="btn btn-primary">Use as initial state</button>
            <a className="btn btn-link" href={config.indexUrl}>Back to the universe</a>
          </div>
        </form>
      </div>
    </div>
  );
}
