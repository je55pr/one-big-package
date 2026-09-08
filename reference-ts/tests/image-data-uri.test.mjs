import test from "node:test";
import assert from "node:assert/strict";
import { encodeRgbaPng, rgbaPngDataUri } from "../.build/packages/image-data-uri/src/index.js";

test("browser-neutral RGBA encoder emits a PNG and data URI", async () => {
  const rgba = Uint8Array.from([255, 0, 0, 255, 0, 255, 0, 128]);
  const png = await encodeRgbaPng(2, 1, rgba);
  assert.deepEqual([...png.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10]);
  const view = new DataView(png.buffer, png.byteOffset, png.byteLength);
  assert.equal(view.getUint32(16, false), 2);
  assert.equal(view.getUint32(20, false), 1);
  assert.match(await rgbaPngDataUri(2, 1, rgba), /^data:image\/png;base64,/);
});
