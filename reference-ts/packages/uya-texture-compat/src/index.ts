import { readGcLevelTextures, TEXTURE_ENTRY_SIZE, type GcLevelTexture, type GcTextureTable } from "../../gc-level-textures/src/index.js";
import type { GcLevelCore } from "../../gc-level-core/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import type { UyaDecodedCoreDataPublicLead } from "../../uya-core-decode/src/index.js";
import type { UyaLevelCoreFieldsPublicLead, UyaPublicLeadArrayRange } from "../../uya-level-core/src/index.js";

export interface UyaTextureEntryPrerequisiteCensus {
  readonly declaredTextureCount: number;
  readonly tableOffset: number;
  readonly tableByteLength: number;
  readonly tableWithinCoreIndex: boolean;
  readonly invalidDimensionCount: number;
  readonly invalidPixelRangeCount: number;
  readonly invalidPaletteRangeCount: number;
  readonly parserEligibleEntryCount: number;
  readonly parserEligibleIndices: readonly number[];
  readonly typeIds: readonly number[];
  readonly minWidth?: number;
  readonly maxWidth?: number;
  readonly minHeight?: number;
  readonly maxHeight?: number;
  readonly minPaletteSlot?: number;
  readonly maxPaletteSlot?: number;
}

export interface UyaGcTextureCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-texture-parser-applied-to-uya-candidate-bytes-with-prerequisite-census";
  readonly table: GcTextureTable;
  readonly publicRange: UyaPublicLeadArrayRange;
  readonly publicTexturesBaseOffset: number;
  readonly gsRamByteLength: number;
  readonly gsRamSha256: string;
  readonly prerequisiteCensus: UyaTextureEntryPrerequisiteCensus;
  readonly parserDecodedTextureCount: number;
  readonly parserDecodedIndices: readonly number[];
  readonly parserDecodedAllEligibleEntries: boolean;
  readonly decodedRgbaSha256: string;
  readonly decodedRgbaByteLength: number;
}

export interface UyaGcTextureCompatibilityOptions {
  readonly maxDimension?: number;
}

/**
 * Apply the unchanged retail-GC level texture reader to UYA decoded core bytes.
 *
 * The public-derived UYA LevelCoreHeader exposes the same candidate ArrayRange texture tables and
 * texturesBaseOffset as GC. This wrapper does not promote those semantic labels by itself. Instead
 * it independently checks the exact prerequisites the GC reader uses (table bounds, dimensions,
 * pixel span, palette span), then compares that census with the unchanged GC reader's output.
 */
export function probeUyaGcTextureCompatibilityPublicLead(
  decoded: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
  gsRam: Uint8Array,
  table: GcTextureTable = "tfrag",
  options: UyaGcTextureCompatibilityOptions = {},
): UyaGcTextureCompatibilityPublicLead {
  const maxDimension = options.maxDimension ?? 1024;
  if (!Number.isSafeInteger(maxDimension) || maxDimension <= 0) throw new RangeError("maxDimension must be a positive safe integer.");
  const range = tableRange(publicFields, table);
  const prerequisiteCensus = censusGcTextureReaderPrerequisites(
    decoded.coreIndex,
    decoded.assets,
    gsRam,
    range,
    publicFields.texturesBaseOffset,
    maxDimension,
  );

  const compatibilityCore = {
    coreHeader: {
      tfragTextures: publicFields.tfragTextures,
      mobyTextures: publicFields.mobyTextures,
      tieTextures: publicFields.tieTextures,
      shrubTextures: publicFields.shrubTextures,
      partTextures: publicFields.partTextures,
      fxTextures: publicFields.fxTextures,
      texturesBaseOffset: publicFields.texturesBaseOffset,
    },
    index: decoded.coreIndex,
    assets: decoded.assets,
    gsRam,
  } as unknown as GcLevelCore;

  // Deliberately unchanged retail-GC texture parser.
  const textures = readGcLevelTextures(compatibilityCore, table, { maxDimension });
  const parserDecodedIndices = textures.map((texture) => texture.index);
  const parserDecodedAllEligibleEntries = arraysEqual(parserDecodedIndices, prerequisiteCensus.parserEligibleIndices);

  let decodedRgbaByteLength = 0;
  const rgbaHash = new Sha256();
  for (const texture of textures) {
    decodedRgbaByteLength += texture.rgba.length;
    rgbaHash.update(texture.rgba);
  }

  return {
    evidenceStatus: "existing-gc-texture-parser-applied-to-uya-candidate-bytes-with-prerequisite-census",
    table,
    publicRange: range,
    publicTexturesBaseOffset: publicFields.texturesBaseOffset,
    gsRamByteLength: gsRam.length,
    gsRamSha256: sha256(gsRam),
    prerequisiteCensus,
    parserDecodedTextureCount: textures.length,
    parserDecodedIndices,
    parserDecodedAllEligibleEntries,
    decodedRgbaSha256: rgbaHash.digestHex(),
    decodedRgbaByteLength,
  };
}

export function censusGcTextureReaderPrerequisites(
  indexBytes: Uint8Array,
  assets: Uint8Array,
  gsRam: Uint8Array,
  range: UyaPublicLeadArrayRange,
  texturesBaseOffset: number,
  maxDimension = 1024,
): UyaTextureEntryPrerequisiteCensus {
  if (!Number.isSafeInteger(range.count) || range.count < 0) throw new RangeError(`Texture count ${range.count} is invalid.`);
  if (!Number.isSafeInteger(range.offset) || range.offset < 0) throw new RangeError(`Texture table offset ${range.offset} is invalid.`);
  const tableByteLength = range.count * TEXTURE_ENTRY_SIZE;
  if (!Number.isSafeInteger(tableByteLength)) throw new RangeError("Texture table byte length exceeds safe integer range.");
  const tableWithinCoreIndex = range.offset <= indexBytes.length && tableByteLength <= indexBytes.length - range.offset;
  if (!tableWithinCoreIndex) {
    return {
      declaredTextureCount: range.count,
      tableOffset: range.offset,
      tableByteLength,
      tableWithinCoreIndex,
      invalidDimensionCount: 0,
      invalidPixelRangeCount: 0,
      invalidPaletteRangeCount: 0,
      parserEligibleEntryCount: 0,
      parserEligibleIndices: [],
      typeIds: [],
    };
  }

  const view = new DataView(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength);
  let invalidDimensionCount = 0;
  let invalidPixelRangeCount = 0;
  let invalidPaletteRangeCount = 0;
  const parserEligibleIndices: number[] = [];
  const typeIds = new Set<number>();
  let minWidth: number | undefined;
  let maxWidth: number | undefined;
  let minHeight: number | undefined;
  let maxHeight: number | undefined;
  let minPaletteSlot: number | undefined;
  let maxPaletteSlot: number | undefined;

  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * TEXTURE_ENTRY_SIZE;
    const dataOffset = view.getInt32(at, true);
    const width = view.getInt16(at + 4, true);
    const height = view.getInt16(at + 6, true);
    const type = view.getInt16(at + 8, true);
    const paletteSlot = view.getInt16(at + 10, true);
    typeIds.add(type);

    if (width > 0) {
      minWidth = minWidth === undefined ? width : Math.min(minWidth, width);
      maxWidth = maxWidth === undefined ? width : Math.max(maxWidth, width);
    }
    if (height > 0) {
      minHeight = minHeight === undefined ? height : Math.min(minHeight, height);
      maxHeight = maxHeight === undefined ? height : Math.max(maxHeight, height);
    }
    if (paletteSlot >= 0) {
      minPaletteSlot = minPaletteSlot === undefined ? paletteSlot : Math.min(minPaletteSlot, paletteSlot);
      maxPaletteSlot = maxPaletteSlot === undefined ? paletteSlot : Math.max(maxPaletteSlot, paletteSlot);
    }

    if (width <= 0 || height <= 0 || width > maxDimension || height > maxDimension) {
      invalidDimensionCount++;
      continue;
    }
    const pixelBytes = width * height;
    const pixelStart = texturesBaseOffset + dataOffset;
    if (!Number.isSafeInteger(pixelStart) || pixelStart < 0 || pixelStart > assets.length || pixelBytes > assets.length - pixelStart) {
      invalidPixelRangeCount++;
      continue;
    }
    const paletteStart = paletteSlot * 0x100;
    const paletteBytes = 256 * 4;
    if (!Number.isSafeInteger(paletteStart) || paletteStart < 0 || paletteStart > gsRam.length || paletteBytes > gsRam.length - paletteStart) {
      invalidPaletteRangeCount++;
      continue;
    }
    parserEligibleIndices.push(i);
  }

  return {
    declaredTextureCount: range.count,
    tableOffset: range.offset,
    tableByteLength,
    tableWithinCoreIndex,
    invalidDimensionCount,
    invalidPixelRangeCount,
    invalidPaletteRangeCount,
    parserEligibleEntryCount: parserEligibleIndices.length,
    parserEligibleIndices,
    typeIds: [...typeIds].sort((a, b) => a - b),
    ...(minWidth !== undefined ? { minWidth } : {}),
    ...(maxWidth !== undefined ? { maxWidth } : {}),
    ...(minHeight !== undefined ? { minHeight } : {}),
    ...(maxHeight !== undefined ? { maxHeight } : {}),
    ...(minPaletteSlot !== undefined ? { minPaletteSlot } : {}),
    ...(maxPaletteSlot !== undefined ? { maxPaletteSlot } : {}),
  };
}

function tableRange(fields: UyaLevelCoreFieldsPublicLead, table: GcTextureTable): UyaPublicLeadArrayRange {
  return {
    tfrag: fields.tfragTextures,
    moby: fields.mobyTextures,
    tie: fields.tieTextures,
    shrub: fields.shrubTextures,
    part: fields.partTextures,
    fx: fields.fxTextures,
  }[table];
}

function arraysEqual(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}

export type { GcLevelTexture };
