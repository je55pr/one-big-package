import test from "node:test";
import assert from "node:assert/strict";
import { HttpRangeRandomAccessReader } from "../.build/packages/importer-common/src/http-range.js";

function exactRangeFetch(source, { totalOverride } = {}) {
  const requests = [];
  const fetchImpl = async (_url, init = {}) => {
    const headers = new Headers(init.headers);
    const range = headers.get("range");
    const match = /^bytes=(\d+)-(\d+)$/.exec(range ?? "");
    if (!match) throw new Error(`missing range header: ${range}`);
    const start = Number(match[1]);
    const end = Number(match[2]);
    requests.push({ range, ifRange: headers.get("if-range") });
    const body = source.slice(start, end + 1);
    const total = typeof totalOverride === "function" ? totalOverride(requests.length) : (totalOverride ?? source.length);
    return new Response(body, {
      status: 206,
      headers: {
        "content-range": `bytes ${start}-${end}/${total}`,
        "content-length": String(body.length),
        etag: '"fixture-v1"',
      },
    });
  };
  return { fetchImpl, requests };
}

test("HTTP range reader establishes size with one byte and reads only requested ranges", async () => {
  const source = Uint8Array.from({ length: 64 }, (_, i) => i);
  const { fetchImpl, requests } = exactRangeFetch(source);
  const reader = await HttpRangeRandomAccessReader.open("https://example.test/disc.iso", {
    fetch: fetchImpl,
    name: "fixture-disc",
  });

  assert.equal(reader.name, "fixture-disc");
  assert.equal(reader.size, 64);
  assert.deepEqual(requests, [{ range: "bytes=0-0", ifRange: null }]);

  assert.deepEqual([...await reader.read(10, 5)], [10, 11, 12, 13, 14]);
  assert.deepEqual(requests[1], { range: "bytes=10-14", ifRange: '"fixture-v1"' });

  const requestCount = requests.length;
  assert.deepEqual([...await reader.read(64, 0)], []);
  assert.equal(requests.length, requestCount, "zero-length reads must not perform HTTP I/O");
});

test("HTTP range reader cancels HTTP 200 without consuming a fallback full body", async () => {
  let cancelled = false;
  let pulled = false;
  const fetchImpl = async () => new Response(new ReadableStream({
    pull() {
      pulled = true;
    },
    cancel() {
      cancelled = true;
    },
  }, { highWaterMark: 0 }), {
    status: 200,
    headers: { "content-length": "999999999" },
  });

  await assert.rejects(
    HttpRangeRandomAccessReader.open("https://example.test/full-fallback.bin", { fetch: fetchImpl }),
    /returned 200; expected 206/,
  );
  assert.equal(cancelled, true);
  assert.equal(pulled, false);
});

test("HTTP range reader rejects wrong Content-Range before consuming the body", async () => {
  let cancelled = false;
  const fetchImpl = async () => new Response(new ReadableStream({
    cancel() {
      cancelled = true;
    },
  }), {
    status: 206,
    headers: {
      "content-range": "bytes 1-1/64",
      "content-length": "1",
    },
  });

  await assert.rejects(
    HttpRangeRandomAccessReader.open("https://example.test/wrong-range.bin", { fetch: fetchImpl }),
    /did not match requested bytes 0-0/,
  );
  assert.equal(cancelled, true);
});

test("HTTP range reader rejects source-size drift between bounded reads", async () => {
  const source = Uint8Array.from({ length: 64 }, (_, i) => i);
  const { fetchImpl } = exactRangeFetch(source, {
    totalOverride: (requestNumber) => requestNumber === 1 ? 64 : 65,
  });
  const reader = await HttpRangeRandomAccessReader.open("https://example.test/drifting.bin", { fetch: fetchImpl });
  await assert.rejects(reader.read(8, 4), /source size changed from 64 to 65/);
});
