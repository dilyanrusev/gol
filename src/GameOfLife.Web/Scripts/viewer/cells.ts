/**
 * Decodes the packed cell string the server sends (see GameOfLife.Core.CellsCodec): a tag
 * character followed by base64. "I" is the sorted indices delta-coded as LEB128 varints, used for
 * sparse views; "B" is a bitmap with one bit per viewport cell, row-major, least significant bit
 * first, used for dense views. Both decode to ascending packed indices (y * width + x).
 */
export function decodeCells(encoded: string, width: number, height: number): number[] {
  if (encoded.length === 0) throw new Error("The encoded cells are empty.");
  const bytes = fromBase64(encoded.slice(1));
  const area = width * height;
  switch (encoded[0]) {
    case "I": return decodeIndices(bytes, area);
    case "B": return decodeBitmap(bytes, area);
    default: throw new Error(`Unknown cell encoding '${encoded[0]}'.`);
  }
}

function fromBase64(text: string): Uint8Array {
  const binary = atob(text);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes;
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
