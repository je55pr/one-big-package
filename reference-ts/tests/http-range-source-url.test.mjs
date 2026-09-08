import test from "node:test";
import assert from "node:assert/strict";
import { normalizeHttpRangeSourceUrl } from "../tools/http-range-source-url.mjs";

test("Google Drive share links normalize to the public usercontent byte-serving endpoint", () => {
  const normalized = normalizeHttpRangeSourceUrl("https://drive.google.com/file/d/FILE123/view?usp=sharing&resourcekey=RK456");
  assert.equal(normalized.origin, "https://drive.usercontent.google.com");
  assert.equal(normalized.pathname, "/download");
  assert.equal(normalized.searchParams.get("id"), "FILE123");
  assert.equal(normalized.searchParams.get("resourcekey"), "RK456");
  assert.equal(normalized.searchParams.get("export"), "download");
  assert.equal(normalized.searchParams.get("confirm"), "t");
});

test("generic HTTP range URLs remain unchanged", () => {
  const input = "https://example.test/disc.iso?token=temporary";
  assert.equal(normalizeHttpRangeSourceUrl(input).toString(), input);
});
