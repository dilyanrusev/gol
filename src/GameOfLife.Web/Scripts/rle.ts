/**
 * A small RLE encoder/decoder for the seed editor (rule B3/S23 only). The server has the
 * authoritative parser; this one exists so the grid and the textarea can stay in sync.
 */
export interface RlePattern {
  width: number;
  height: number;
  /** Live cells as [x, y] relative to the top-left corner. */
  cells: Array<[number, number]>;
}

export function encodeRle(cells: Iterable<[number, number]>, name?: string): string {
  const list = Array.from(cells);
  const lines: string[] = [];
  if (name) lines.push(`#N ${name}`);
  if (list.length === 0) {
    lines.push("x = 0, y = 0, rule = B3/S23", "!");
    return lines.join("\n") + "\n";
  }

  const minX = Math.min(...list.map((c) => c[0]));
  const minY = Math.min(...list.map((c) => c[1]));
  const maxX = Math.max(...list.map((c) => c[0]));
  const maxY = Math.max(...list.map((c) => c[1]));
  lines.push(`x = ${maxX - minX + 1}, y = ${maxY - minY + 1}, rule = B3/S23`);

  const rows = new Map<number, number[]>();
  for (const [x, y] of list) {
    const row = rows.get(y - minY) ?? [];
    row.push(x - minX);
    rows.set(y - minY, row);
  }

  let body = "";
  let line = "";
  const emit = (count: number, tag: string) => {
    if (count <= 0) return;
    const token = count === 1 ? tag : `${count}${tag}`;
    if (line.length + token.length > 70) {
      body += line + "\n";
      line = "";
    }
    line += token;
  };

  let currentY = 0;
  for (const y of [...rows.keys()].sort((a, b) => a - b)) {
    emit(y - currentY, "$");
    currentY = y;
    const xs = [...new Set(rows.get(y)!)].sort((a, b) => a - b);
    let x = 0;
    let i = 0;
    while (i < xs.length) {
      emit(xs[i] - x, "b");
      const start = i;
      while (i + 1 < xs.length && xs[i + 1] === xs[i] + 1) i++;
      emit(i - start + 1, "o");
      x = xs[i] + 1;
      i++;
    }
  }
  emit(1, "!");
  body += line;
  lines.push(body);
  return lines.join("\n") + "\n";
}

export function decodeRle(text: string): RlePattern {
  const lines = text.split(/\r?\n/);
  let headerSeen = false;
  let width = 0;
  let height = 0;
  const cells: Array<[number, number]> = [];
  let x = 0;
  let y = 0;
  let run = 0;

  outer: for (const raw of lines) {
    if (raw.startsWith("#")) continue;
    if (!headerSeen) {
      if (raw.trim() === "") continue;
      const m = /^\s*x\s*=\s*(\d+)\s*,\s*y\s*=\s*(\d+)\s*(?:,\s*rule\s*=\s*([^\s,]+))?\s*$/i.exec(raw);
      if (!m) throw new Error("Expected a header like 'x = 3, y = 3, rule = B3/S23'.");
      width = Number(m[1]);
      height = Number(m[2]);
      if (m[3] && !/^(b3\/s23|23\/3|s23\/b3)$/i.test(m[3])) throw new Error(`Unsupported rule '${m[3]}'. Only B3/S23 is supported.`);
      headerSeen = true;
      continue;
    }
    for (const ch of raw) {
      if (/\s/.test(ch)) continue;
      if (ch >= "0" && ch <= "9") {
        run = run * 10 + (ch.charCodeAt(0) - 48);
        continue;
      }
      const count = run === 0 ? 1 : run;
      run = 0;
      if (ch === "!") break outer;
      if (ch === "$") { y += count; x = 0; continue; }
      if (ch === "b" || ch === "B") { x += count; continue; }
      if (!/[a-zA-Z]/.test(ch)) throw new Error(`Unexpected character '${ch}'.`);
      for (let i = 0; i < count; i++) cells.push([x + i, y]);
      x += count;
    }
  }
  if (!headerSeen) throw new Error("The RLE header line ('x = ..., y = ...') is missing.");
  for (const [cx, cy] of cells) {
    width = Math.max(width, cx + 1);
    height = Math.max(height, cy + 1);
  }
  return { width, height, cells };
}
