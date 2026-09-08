import type { RandomAccessReader } from "../../importer-common/src/index.js";

const SHA256_BLOCK_BYTES = 64;
const DEFAULT_HASH_CHUNK_BYTES = 8 * 1024 * 1024;

const INITIAL_STATE = new Uint32Array([
  0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
  0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
]);

const ROUND_CONSTANTS = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
  0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
  0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
  0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
  0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
  0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
  0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
  0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

export interface RandomAccessHashProgress {
  readonly bytesRead: number;
  readonly totalBytes: number;
}

export interface RandomAccessHashOptions {
  /** Maximum byte range requested from the source at once. */
  readonly chunkSize?: number;
  readonly signal?: AbortSignal;
  readonly onProgress?: (progress: RandomAccessHashProgress) => void;
}

export interface Sha256HashResult {
  readonly sizeBytes: number;
  readonly sha256: string;
}

/** Incremental SHA-256 with no dependency on Node crypto or whole-buffer Web Crypto digests. */
export class Sha256 {
  private readonly state = new Uint32Array(INITIAL_STATE);
  private readonly block = new Uint8Array(SHA256_BLOCK_BYTES);
  private readonly words = new Uint32Array(64);
  private blockLength = 0;
  private bytesHashed = 0;
  private finalDigest: Uint8Array | undefined;

  update(data: Uint8Array): this {
    if (this.finalDigest) throw new Error("SHA-256 digest has already been finalized.");
    if (!Number.isSafeInteger(data.length) || data.length < 0 || this.bytesHashed > Number.MAX_SAFE_INTEGER - data.length) {
      throw new RangeError("SHA-256 input length exceeds JavaScript safe integer range.");
    }

    this.bytesHashed += data.length;
    let offset = 0;

    if (this.blockLength > 0) {
      const copyLength = Math.min(SHA256_BLOCK_BYTES - this.blockLength, data.length);
      this.block.set(data.subarray(0, copyLength), this.blockLength);
      this.blockLength += copyLength;
      offset += copyLength;
      if (this.blockLength === SHA256_BLOCK_BYTES) {
        this.transform(this.block, 0);
        this.blockLength = 0;
      }
    }

    while (offset + SHA256_BLOCK_BYTES <= data.length) {
      this.transform(data, offset);
      offset += SHA256_BLOCK_BYTES;
    }

    if (offset < data.length) {
      this.block.set(data.subarray(offset), 0);
      this.blockLength = data.length - offset;
    }

    return this;
  }

  digest(): Uint8Array {
    if (this.finalDigest) return this.finalDigest.slice();

    const bitLength = BigInt(this.bytesHashed) * 8n;
    this.block[this.blockLength] = 0x80;
    this.blockLength += 1;

    if (this.blockLength > 56) {
      this.block.fill(0, this.blockLength, SHA256_BLOCK_BYTES);
      this.transform(this.block, 0);
      this.blockLength = 0;
    }

    this.block.fill(0, this.blockLength, 56);
    for (let index = 0; index < 8; index++) {
      this.block[63 - index] = Number((bitLength >> BigInt(index * 8)) & 0xffn);
    }
    this.transform(this.block, 0);

    const output = new Uint8Array(32);
    const view = new DataView(output.buffer);
    for (let index = 0; index < 8; index++) view.setUint32(index * 4, this.state[index]!, false);
    this.finalDigest = output;
    return output.slice();
  }

  digestHex(): string {
    let output = "";
    for (const byte of this.digest()) output += byte.toString(16).padStart(2, "0");
    return output;
  }

  private transform(bytes: Uint8Array, offset: number): void {
    const words = this.words;
    for (let index = 0; index < 16; index++) {
      const start = offset + index * 4;
      words[index] = (
        (bytes[start]! << 24)
        | (bytes[start + 1]! << 16)
        | (bytes[start + 2]! << 8)
        | bytes[start + 3]!
      ) >>> 0;
    }

    for (let index = 16; index < 64; index++) {
      const word15 = words[index - 15]!;
      const word2 = words[index - 2]!;
      const sigma0 = rotateRight(word15, 7) ^ rotateRight(word15, 18) ^ (word15 >>> 3);
      const sigma1 = rotateRight(word2, 17) ^ rotateRight(word2, 19) ^ (word2 >>> 10);
      words[index] = (words[index - 16]! + sigma0 + words[index - 7]! + sigma1) >>> 0;
    }

    let a = this.state[0]!;
    let b = this.state[1]!;
    let c = this.state[2]!;
    let d = this.state[3]!;
    let e = this.state[4]!;
    let f = this.state[5]!;
    let g = this.state[6]!;
    let h = this.state[7]!;

    for (let index = 0; index < 64; index++) {
      const sum1 = rotateRight(e, 6) ^ rotateRight(e, 11) ^ rotateRight(e, 25);
      const choose = (e & f) ^ (~e & g);
      const temp1 = (h + sum1 + choose + ROUND_CONSTANTS[index]! + words[index]!) >>> 0;
      const sum0 = rotateRight(a, 2) ^ rotateRight(a, 13) ^ rotateRight(a, 22);
      const majority = (a & b) ^ (a & c) ^ (b & c);
      const temp2 = (sum0 + majority) >>> 0;

      h = g;
      g = f;
      f = e;
      e = (d + temp1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (temp1 + temp2) >>> 0;
    }

    this.state[0] = (this.state[0]! + a) >>> 0;
    this.state[1] = (this.state[1]! + b) >>> 0;
    this.state[2] = (this.state[2]! + c) >>> 0;
    this.state[3] = (this.state[3]! + d) >>> 0;
    this.state[4] = (this.state[4]! + e) >>> 0;
    this.state[5] = (this.state[5]! + f) >>> 0;
    this.state[6] = (this.state[6]! + g) >>> 0;
    this.state[7] = (this.state[7]! + h) >>> 0;
  }
}

/**
 * Hash any random-access source using bounded sequential reads. A ConcatenatedRandomAccessReader can
 * therefore hash a complete split ISO without reconstructing it or allocating the full payload.
 */
export async function hashRandomAccessReaderSha256(
  reader: RandomAccessReader,
  options: RandomAccessHashOptions = {},
): Promise<Sha256HashResult> {
  const chunkSize = options.chunkSize ?? DEFAULT_HASH_CHUNK_BYTES;
  if (!Number.isSafeInteger(chunkSize) || chunkSize <= 0) throw new RangeError(`Invalid SHA-256 chunkSize: ${chunkSize}`);

  const hash = new Sha256();
  let offset = 0;
  while (offset < reader.size) {
    if (options.signal?.aborted) throw options.signal.reason ?? new Error("SHA-256 hashing was aborted.");
    const length = Math.min(chunkSize, reader.size - offset);
    const bytes = await reader.read(offset, length);
    if (bytes.length !== length) {
      throw new Error(`Short read while hashing ${reader.name} @ ${offset}+${length}: got ${bytes.length} bytes.`);
    }
    hash.update(bytes);
    offset += length;
    options.onProgress?.({ bytesRead: offset, totalBytes: reader.size });
  }

  return { sizeBytes: reader.size, sha256: hash.digestHex() };
}

function rotateRight(value: number, bits: number): number {
  return ((value >>> bits) | (value << (32 - bits))) >>> 0;
}
