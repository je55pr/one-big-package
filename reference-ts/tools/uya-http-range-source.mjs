import { MappedRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { HttpRangeRandomAccessReader } from "../.build/packages/importer-common/src/http-range.js";
import { normalizeHttpRangeSourceUrl } from "./http-range-source-url.mjs";
import { UYA_NTSCU_ORIGINAL_AUTHORITY } from "./uya-retail-authority.mjs";

const HTTP_HEADERS = Object.freeze({
  "Accept-Encoding": "identity",
  "User-Agent": "OBP-UYA-HTTP-Range-Probe/1.0",
});

export function hasUyaHttpRangeSourceEnv(env = process.env) {
  return Boolean(env.OBP_UYA_HTTP_RANGE_URL?.trim() || env.OBP_UYA_HTTP_RANGE_PARTS_JSON?.trim());
}

export function parseUyaHttpRangePartSpecs(text) {
  let value;
  try {
    value = JSON.parse(text);
  } catch (error) {
    throw new Error(`OBP_UYA_HTTP_RANGE_PARTS_JSON must be valid JSON: ${error instanceof Error ? error.message : String(error)}`);
  }
  if (!Array.isArray(value) || value.length === 0) {
    throw new Error("OBP_UYA_HTTP_RANGE_PARTS_JSON must be a non-empty JSON array.");
  }

  const seen = new Set();
  const specs = value.map((item, index) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      throw new Error(`HTTP range part entry ${index} must be an object.`);
    }
    const partNumber = item.partNumber;
    const url = item.url;
    if (!Number.isSafeInteger(partNumber) || partNumber < 1 || partNumber > UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkCount) {
      throw new Error(`HTTP range part entry ${index} has invalid partNumber '${partNumber}'.`);
    }
    if (seen.has(partNumber)) throw new Error(`HTTP range part ${partNumber} was supplied more than once.`);
    seen.add(partNumber);
    if (typeof url !== "string" || url.trim() === "") {
      throw new Error(`HTTP range part ${partNumber} must provide a non-empty url.`);
    }
    return { partNumber, url: url.trim() };
  });

  return specs.sort((a, b) => a.partNumber - b.partNumber);
}

export function expectedUyaRetailSplitPartBytes(partNumber) {
  if (!Number.isSafeInteger(partNumber) || partNumber < 1 || partNumber > UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkCount) {
    throw new RangeError(`UYA retail split part number must be 1..${UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkCount}.`);
  }
  return partNumber === UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkCount
    ? UYA_NTSCU_ORIGINAL_AUTHORITY.finalSplitChunkBytes
    : UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkBytes;
}

/**
 * Open either one whole-disc URL or selected known retail split chunks as a strict logical disc.
 * URLs remain private to the HTTP readers and are never included in returned source metadata.
 */
export async function openUyaHttpRangeSourceFromEnv(
  env = process.env,
  { openReader = defaultOpenReader } = {},
) {
  const wholeUrl = env.OBP_UYA_HTTP_RANGE_URL?.trim();
  const partsJson = env.OBP_UYA_HTTP_RANGE_PARTS_JSON?.trim();
  if (wholeUrl && partsJson) {
    throw new Error("OBP_UYA_HTTP_RANGE_URL and OBP_UYA_HTTP_RANGE_PARTS_JSON are mutually exclusive.");
  }
  if (!wholeUrl && !partsJson) return undefined;

  if (wholeUrl) {
    const reader = await openReader(normalizeHttpRangeSourceUrl(wholeUrl), "uya-http-range-disc");
    if (reader.size !== UYA_NTSCU_ORIGINAL_AUTHORITY.fullIsoBytes) {
      throw new Error(
        `UYA whole-disc HTTP source size mismatch: expected ${UYA_NTSCU_ORIGINAL_AUTHORITY.fullIsoBytes}, got ${reader.size}.`,
      );
    }
    return {
      disc: reader,
      sourceMode: "http-range-source",
      sourceParts: [{
        index: 0,
        name: reader.name,
        sizeBytes: reader.size,
        logicalStartBytes: 0,
        transport: "http-range",
      }],
    };
  }

  const specs = parseUyaHttpRangePartSpecs(partsJson);
  const mapped = [];
  const sourceParts = [];
  for (const spec of specs) {
    const reader = await openReader(normalizeHttpRangeSourceUrl(spec.url), `uya-http-range-part-${spec.partNumber}`);
    const expectedSize = expectedUyaRetailSplitPartBytes(spec.partNumber);
    if (reader.size !== expectedSize) {
      throw new Error(`UYA HTTP split part ${spec.partNumber} size mismatch: expected ${expectedSize}, got ${reader.size}.`);
    }
    const logicalStartBytes = (spec.partNumber - 1) * UYA_NTSCU_ORIGINAL_AUTHORITY.splitChunkBytes;
    mapped.push({ start: logicalStartBytes, reader });
    sourceParts.push({
      partNumber: spec.partNumber,
      name: reader.name,
      sizeBytes: reader.size,
      logicalStartBytes,
      transport: "http-range",
    });
  }

  return {
    disc: new MappedRandomAccessReader(
      mapped,
      UYA_NTSCU_ORIGINAL_AUTHORITY.fullIsoBytes,
      "uya-http-range-sparse-split-disc",
    ),
    sourceMode: "http-range-sparse-mapped-split-parts",
    sourceParts,
  };
}

async function defaultOpenReader(url, name) {
  return HttpRangeRandomAccessReader.open(url, {
    name,
    headers: HTTP_HEADERS,
  });
}
