import { open, stat } from "node:fs/promises";

/**
 * Node fs-backed {@link RandomAccessReader} for local archaeology tools.
 *
 * Only the requested byte ranges are read; the multi-gigabyte source is never
 * materialised. This mirrors the browser `BlobRandomAccessReader` contract so the
 * same importer / ISO / ELF code paths run unchanged against a retail ISO on disk.
 */
export class LocalFileRandomAccessReader {
  /** @param {import("node:fs/promises").FileHandle} handle */
  constructor(handle, name, size) {
    this.handle = handle;
    this.name = name;
    this.size = size;
  }

  static async open(path, name) {
    const info = await stat(path);
    if (!info.isFile()) throw new Error(`Not a file: ${path}`);
    const handle = await open(path, "r");
    return new LocalFileRandomAccessReader(handle, name ?? path.split(/[\\/]/).at(-1) ?? path, info.size);
  }

  async read(offset, length) {
    if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset > this.size || length > this.size - offset) {
      throw new RangeError(`Invalid read ${this.name} @ ${offset}+${length} (size ${this.size})`);
    }
    if (length === 0) return new Uint8Array();
    const buffer = Buffer.allocUnsafe(length);
    let filled = 0;
    while (filled < length) {
      const { bytesRead } = await this.handle.read(buffer, filled, length - filled, offset + filled);
      if (bytesRead === 0) break;
      filled += bytesRead;
    }
    if (filled !== length) throw new Error(`Short local read from ${this.name} @ ${offset}+${length}: got ${filled} bytes.`);
    return new Uint8Array(buffer.buffer, buffer.byteOffset, length);
  }

  async close() {
    await this.handle.close();
  }
}
