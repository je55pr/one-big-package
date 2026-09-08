/** Browser-neutral helpers for embedding decoded native RGBA textures in OBP materials. */

const PNG_SIGNATURE = Uint8Array.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = (c & 1) !== 0 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

/** Encode an 8-bit RGBA image as a standards-compliant PNG using the Web Compression API. */
export async function encodeRgbaPng(width: number, height: number, rgba: Uint8Array): Promise<Uint8Array> {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0) {
    throw new Error(`PNG dimensions must be positive integers, got ${width}x${height}.`);
  }
  const expected = width * height * 4;
  if (!Number.isSafeInteger(expected) || rgba.length !== expected) {
    throw new Error(`RGBA buffer is ${rgba.length} bytes, expected ${expected} for ${width}x${height}.`);
  }

  const stride = width * 4 + 1;
  const raw = new Uint8Array(stride * height);
  for (let y = 0; y < height; y++) {
    raw[y * stride] = 0;
    raw.set(rgba.subarray(y * width * 4, (y + 1) * width * 4), y * stride + 1);
  }

  if (typeof CompressionStream === "undefined") {
    throw new Error("PNG encoding requires the Web CompressionStream API.");
  }
  const compressedStream = new Blob([raw]).stream().pipeThrough(new CompressionStream("deflate"));
  const idat = new Uint8Array(await new Response(compressedStream).arrayBuffer());

  const ihdr = new Uint8Array(13);
  const hv = new DataView(ihdr.buffer);
  hv.setUint32(0, width, false);
  hv.setUint32(4, height, false);
  ihdr[8] = 8;
  ihdr[9] = 6;

  return concatBytes([
    PNG_SIGNATURE,
    pngChunk("IHDR", ihdr),
    pngChunk("IDAT", idat),
    pngChunk("IEND", new Uint8Array()),
  ]);
}

export async function rgbaPngDataUri(width: number, height: number, rgba: Uint8Array): Promise<string> {
  return `data:image/png;base64,${base64Encode(await encodeRgbaPng(width, height, rgba))}`;
}

function pngChunk(type: string, data: Uint8Array): Uint8Array {
  if (type.length !== 4) throw new Error(`PNG chunk type '${type}' must be four ASCII bytes.`);
  const typeBytes = Uint8Array.from(type, (char) => char.charCodeAt(0));
  const out = new Uint8Array(12 + data.length);
  const view = new DataView(out.buffer);
  view.setUint32(0, data.length, false);
  out.set(typeBytes, 4);
  out.set(data, 8);
  const crcInput = new Uint8Array(typeBytes.length + data.length);
  crcInput.set(typeBytes, 0);
  crcInput.set(data, typeBytes.length);
  view.setUint32(8 + data.length, crc32(crcInput), false);
  return out;
}

function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (const byte of bytes) c = CRC_TABLE[(c ^ byte) & 0xff]! ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function concatBytes(parts: readonly Uint8Array[]): Uint8Array {
  const total = parts.reduce((sum, part) => sum + part.length, 0);
  const out = new Uint8Array(total);
  let at = 0;
  for (const part of parts) {
    out.set(part, at);
    at += part.length;
  }
  return out;
}

function base64Encode(bytes: Uint8Array): string {
  let binary = "";
  const chunk = 0x8000;
  for (let at = 0; at < bytes.length; at += chunk) {
    const slice = bytes.subarray(at, Math.min(bytes.length, at + chunk));
    binary += String.fromCharCode(...slice);
  }
  return btoa(binary);
}
