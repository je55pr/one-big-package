import { createHash } from "node:crypto";
import { UYA_DISC_SECTOR_BYTES } from "../.build/packages/uya-disc-toc/src/index.js";

export const UYA_NTSCU_ORIGINAL_AUTHORITY = Object.freeze({
  label: "Ratchet & Clank: Up Your Arsenal NTSC-U original",
  serial: "SCUS-97353",
  fullIsoBytes: 4_379_377_664,
  fullIsoSha256: "d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444",
  splitChunkBytes: 503_316_480,
  splitChunkCount: 9,
  finalSplitChunkBytes: 352_845_824,
  bootExecutableName: "SCUS_973.53",
  bootExecutableLba: 1463,
  bootExecutableBytes: 771_008,
  bootExecutableSha256: "200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d",
  tocLba: 1001,
  tocWindowBytes: 0x200000,
  tocWindowSha256: "a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a",
});

/**
 * Verify the exact bounded resident-ToC window that has already been recorded from the
 * supported retail authority image. This is an identity/provenance guard only: passing it
 * does not promote the semantics of LBA 1001 or any parsed field to native loader truth.
 */
export async function assertUyaAuthorityTocWindow(reader, authority = UYA_NTSCU_ORIGINAL_AUTHORITY) {
  const tocLba = requireNonNegativeSafeInteger(authority.tocLba, "authority.tocLba");
  const windowBytes = requirePositiveSafeInteger(authority.tocWindowBytes, "authority.tocWindowBytes");
  const expectedSha256 = requireSha256(authority.tocWindowSha256, "authority.tocWindowSha256");
  const offsetBytes = tocLba * UYA_DISC_SECTOR_BYTES;
  if (!Number.isSafeInteger(offsetBytes)) throw new RangeError("Authority TOC byte offset exceeds JavaScript safe integer range.");
  if (offsetBytes > reader.size || windowBytes > reader.size - offsetBytes) {
    throw new RangeError(`Authority TOC window ${offsetBytes}+${windowBytes} lies outside ${reader.name} (${reader.size} bytes).`);
  }

  const hash = createHash("sha256");
  const chunkBytes = 1024 * 1024;
  let cursor = 0;
  while (cursor < windowBytes) {
    const take = Math.min(chunkBytes, windowBytes - cursor);
    const bytes = await reader.read(offsetBytes + cursor, take);
    if (bytes.length !== take) {
      throw new Error(`Short authority TOC read from ${reader.name} @ ${offsetBytes + cursor}+${take}: got ${bytes.length} bytes.`);
    }
    hash.update(bytes);
    cursor += take;
  }

  const actualSha256 = hash.digest("hex");
  if (actualSha256 !== expectedSha256) {
    throw new Error(
      `UYA authority TOC identity mismatch at LBA ${tocLba}: expected ${expectedSha256}, got ${actualSha256}. ` +
      "Refusing to apply UYA semantic probes to this source.",
    );
  }

  return {
    label: authority.label,
    serial: authority.serial,
    tocLba,
    offsetBytes,
    windowBytes,
    sha256: actualSha256,
    matched: true,
    evidenceScope: "bounded retail identity window only; native loader provenance of this address remains unproven",
  };
}

function requireNonNegativeSafeInteger(value, label) {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}

function requirePositiveSafeInteger(value, label) {
  if (!Number.isSafeInteger(value) || value <= 0) throw new RangeError(`${label} must be a positive safe integer.`);
  return value;
}

function requireSha256(value, label) {
  if (typeof value !== "string" || !/^[0-9a-f]{64}$/i.test(value)) throw new Error(`${label} must be a 64-digit hexadecimal SHA-256.`);
  return value.toLowerCase();
}
