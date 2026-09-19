const INDICES_TAG = 0x49; // "I"
const BITMAP_TAG = 0x42; // "B"

/** What the server sends for a view with no live cells: the indices tag and nothing after it. */
export const EMPTY_CELLS: Uint8Array = new Uint8Array([INDICES_TAG]);

/**
 * Decodes the packed cells the server sends (see GameOfLife.Core.CellsCodec): a tag byte followed
 * by the payload, delivered as-is by MessagePack. "I" is the sorted indices delta-coded as LEB128
 * varints, used for sparse views; "B" is a bitmap with one bit per viewport cell, row-major, least
 * significant bit first, used for dense views. Both decode to ascending packed indices (y * width + x).
 */
export function decodeCells(encoded: Uint8Array, width: number, height: number): number[] {
  if (encoded.length === 0) throw new Error("The encoded cells are empty.");
  const bytes = encoded.subarray(1);
  const area = width * height;
  switch (encoded[0]) {
    case INDICES_TAG: return decodeIndices(bytes, area);
    case BITMAP_TAG: return decodeBitmap(bytes, area);
    default: throw new Error(`Unknown cell encoding ${encoded[0]}.`);
  }
}

function decodeIndices(bytes: Uint8Array, area: number): number[] {
  const indices: number[] = [];
  let previous = 0;
  let position = 0;
  while (position < bytes.length) {
    let delta = 0;
    let shift = 0;
    let byte: number;
    do {
      if (position === bytes.length) throw new Error("Truncated varint.");
      byte = bytes[position++];
      delta += (byte & 0x7f) * 2 ** shift;
      shift += 7;
    } while (byte & 0x80);
    const index = previous + delta;
    if (index >= area) throw new Error("Index outside the viewport.");
    indices.push(index);
    previous = index;
  }
  return indices;
}

function decodeBitmap(bytes: Uint8Array, area: number): number[] {
  if (bytes.length !== (area + 7) >> 3) throw new Error("Bitmap length does not match the viewport.");
  const indices: number[] = [];
  for (let index = 0; index < area; index++) {
    if (bytes[index >> 3] & (1 << (index & 7))) indices.push(index);
  }
  return indices;
}
