import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS,
  readUyaLevelWadHeaderPublicLead,
} from "../.build/packages/uya-level-wad/src/index.js";

test("UYA 0x60 public-lead range names match the pinned Wrench GC/UYA header", async () => {
  assert.deepEqual(UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS, [
    "primary",
    "core-bank",
    "gameplay",
    "occlusion",
    "chunk-0",
    "chunk-1",
    "chunk-2",
    "chunk-sound-bank-0",
    "chunk-sound-bank-1",
    "chunk-sound-bank-2",
  ]);

  const bytes = new Uint8Array(0x800 * 2);
  const view = new DataView(bytes.buffer);
  view.setInt32(0x00, 0x60, true);
  view.setInt32(0x10, 1, true);
  view.setInt32(0x14, 1, true);
  const header = await readUyaLevelWadHeaderPublicLead(
    new BlobRandomAccessReader(new Blob([bytes]), "uya-outer-public-lead.bin"),
  );
  assert.equal(header.ranges[0].publicLabel, "primary");
  assert.equal(header.ranges[1].publicLabel, "core-bank");
});
