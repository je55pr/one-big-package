import type { RandomAccessReader } from "./index.js";

export interface HttpRangeRandomAccessReaderOptions {
  readonly name?: string;
  readonly fetch?: typeof globalThis.fetch;
  readonly headers?: HeadersInit;
}

interface HttpRangeResult {
  readonly bytes: Uint8Array;
  readonly totalSize: number;
  readonly etag?: string;
  readonly lastModified?: string;
}

function parseContentRange(value: string | null): { start: number; end: number; total: number } {
  const match = /^bytes (\d+)-(\d+)\/(\d+)$/.exec(value ?? "");
  if (!match) throw new Error(`Invalid or missing Content-Range header '${value ?? ""}'.`);
  const start = Number(match[1]);
  const end = Number(match[2]);
  const total = Number(match[3]);
  if (![start, end, total].every(Number.isSafeInteger) || start < 0 || end < start || total <= end) {
    throw new Error(`Invalid Content-Range values '${value}'.`);
  }
  return { start, end, total };
}

async function cancelResponseBody(response: Response): Promise<void> {
  try {
    await response.body?.cancel();
  } catch {
    // Best effort only. A rejected range response is never intentionally consumed.
  }
}

async function readExactBody(response: Response, expectedLength: number): Promise<Uint8Array> {
  if (!response.body) throw new Error("HTTP 206 response had no body stream.");
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let received = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      if (!value) continue;
      received += value.byteLength;
      if (received > expectedLength) {
        await reader.cancel("HTTP range response exceeded requested byte count");
        throw new Error(`HTTP range body exceeded requested ${expectedLength} bytes; cancelled after ${received}.`);
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  if (received !== expectedLength) {
    throw new Error(`Short HTTP range body: expected ${expectedLength} bytes, received ${received}.`);
  }

  const output = new Uint8Array(received);
  let offset = 0;
  for (const chunk of chunks) {
    output.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return output;
}

async function fetchExactRange(
  fetchFn: typeof globalThis.fetch,
  url: string,
  baseHeaders: Headers,
  start: number,
  length: number,
  validator?: string,
): Promise<HttpRangeResult> {
  if (!Number.isSafeInteger(start) || start < 0 || !Number.isSafeInteger(length) || length <= 0) {
    throw new RangeError(`Invalid HTTP range ${start}+${length}.`);
  }
  const end = start + length - 1;
  if (!Number.isSafeInteger(end)) throw new RangeError("HTTP range end exceeds JavaScript safe integer range.");

  const headers = new Headers(baseHeaders);
  headers.set("Range", `bytes=${start}-${end}`);
  if (validator) headers.set("If-Range", validator);

  const response = await fetchFn(url, { headers, redirect: "follow" });
  if (response.status !== 206) {
    await cancelResponseBody(response);
    throw new Error(`HTTP range request returned ${response.status}; expected 206. Response body was cancelled without reading.`);
  }

  const contentLengthText = response.headers.get("content-length");
  if (contentLengthText !== null) {
    const contentLength = Number(contentLengthText);
    if (!Number.isSafeInteger(contentLength) || contentLength !== length) {
      await cancelResponseBody(response);
      throw new Error(`HTTP range Content-Length ${contentLengthText} did not equal requested ${length}; body cancelled without reading.`);
    }
  }

  const contentRangeText = response.headers.get("content-range");
  const contentRange = parseContentRange(contentRangeText);
  if (contentRange.start !== start || contentRange.end !== end) {
    await cancelResponseBody(response);
    throw new Error(`HTTP range Content-Range '${contentRangeText}' did not match requested bytes ${start}-${end}; body cancelled without reading.`);
  }

  const bytes = await readExactBody(response, length);
  const etag = response.headers.get("etag") ?? undefined;
  const lastModified = response.headers.get("last-modified") ?? undefined;
  return {
    bytes,
    totalSize: contentRange.total,
    ...(etag !== undefined ? { etag } : {}),
    ...(lastModified !== undefined ? { lastModified } : {}),
  };
}

/**
 * Random-access reader backed by strict HTTP byte-range requests.
 *
 * Construction performs a one-byte range request to establish the authoritative total size.
 * Every later read requires HTTP 206, an exact Content-Range, an exact body length, and the same
 * total size. If the origin exposes a strong ETag (or Last-Modified fallback), it is sent via
 * If-Range so source drift becomes a rejected 200 response instead of silently mixing versions.
 */
export class HttpRangeRandomAccessReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;
  private readonly url: string;
  private readonly fetchFn: typeof globalThis.fetch;
  private readonly headers: Headers;
  private readonly validator: string | undefined;

  private constructor(
    url: string,
    size: number,
    fetchFn: typeof globalThis.fetch,
    headers: Headers,
    validator: string | undefined,
    name: string,
  ) {
    this.url = url;
    this.size = size;
    this.fetchFn = fetchFn;
    this.headers = headers;
    this.validator = validator;
    this.name = name;
  }

  static async open(url: string | URL, options: HttpRangeRandomAccessReaderOptions = {}): Promise<HttpRangeRandomAccessReader> {
    const normalizedUrl = new URL(url).toString();
    const fetchFn = options.fetch ?? globalThis.fetch;
    if (typeof fetchFn !== "function") throw new Error("HTTP range reader requires a Fetch API implementation.");
    const headers = new Headers(options.headers);
    headers.delete("Range");
    headers.delete("If-Range");

    const initial = await fetchExactRange(fetchFn, normalizedUrl, headers, 0, 1);
    const validator = initial.etag && !initial.etag.startsWith("W/")
      ? initial.etag
      : initial.lastModified;
    const defaultName = `http-range:${new URL(normalizedUrl).hostname}`;
    return new HttpRangeRandomAccessReader(
      normalizedUrl,
      initial.totalSize,
      fetchFn,
      headers,
      validator,
      options.name ?? defaultName,
    );
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset > this.size || length > this.size - offset) {
      throw new RangeError(`Invalid read ${this.name} @ ${offset}+${length} (size ${this.size})`);
    }
    if (length === 0) return new Uint8Array();

    const result = await fetchExactRange(this.fetchFn, this.url, this.headers, offset, length, this.validator);
    if (result.totalSize !== this.size) {
      throw new Error(`HTTP range source size changed from ${this.size} to ${result.totalSize}.`);
    }
    return result.bytes;
  }
}
