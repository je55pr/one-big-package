import { OBP_SCHEMA_VERSION, type OBPSourceGame, type OBPWorld } from "../../core/src/index.js";

function source(game: OBPSourceGame, levelId: string, assetKind?: string) {
  return {
    game,
    buildId: `${game}-synthetic-v1`,
    levelId,
    ...(assetKind ? { assetKind } : {}),
    notes: ["Synthetic fixture only; contains no game data."],
  } as const;
}

const floorPositions = [
  -6, 0, -5,
   6, 0, -5,
   6, 0,  5,
  -6, 0,  5,
];
const floorIndices = [0, 1, 2, 0, 2, 3];

const rampPositions = [
  -2, 0, -1,
   2, 0, -1,
   2, 2,  2,
  -2, 2,  2,
];
const rampIndices = [0, 1, 2, 0, 2, 3];

function makeWorld(game: "rac1" | "rac2", offsetX: number): OBPWorld {
  const levelId = game === "rac1" ? "fixture-veldin-ish" : "fixture-maktar-ish";
  return {
    schemaVersion: OBP_SCHEMA_VERSION,
    id: `${game}-synthetic-world`,
    displayName: game === "rac1" ? "R&C1 synthetic fixture" : "R&C2 synthetic fixture",
    source: source(game, levelId, "world"),
    bounds: { min: { x: offsetX - 7, y: 0, z: -6 }, max: { x: offsetX + 7, y: 4, z: 6 } },
    materials: [
      {
        id: `${game}-surface`,
        name: `${game.toUpperCase()} debug surface`,
        source: source(game, levelId, "material"),
        debugRgba: game === "rac1" ? [0.26, 0.56, 0.84, 1] : [0.78, 0.43, 0.22, 1],
      },
    ],
    meshes: [
      {
        id: `${game}-floor`,
        name: "Synthetic floor",
        materialId: `${game}-surface`,
        geometry: { positions: translate(floorPositions, offsetX, 0, 0), indices: floorIndices },
        source: source(game, levelId, "tfrag-like"),
      },
      {
        id: `${game}-ramp`,
        name: "Synthetic ramp",
        materialId: `${game}-surface`,
        geometry: { positions: translate(rampPositions, offsetX, 0, 0), indices: rampIndices },
        source: source(game, levelId, "tfrag-like"),
      },
    ],
    collisionMeshes: [
      {
        id: `${game}-collision`,
        name: "Synthetic collision",
        geometry: {
          positions: [...translate(floorPositions, offsetX, 0.02, 0), ...translate(rampPositions, offsetX, 0.02, 0)],
          indices: [...floorIndices, ...rampIndices.map((index) => index + 4)],
        },
        triangleMaterialIds: [0, 0, game === "rac1" ? 1 : 2, game === "rac1" ? 1 : 2],
        source: source(game, levelId, "collision"),
      },
    ],
    instances: [
      {
        id: `${game}-instance-a`,
        name: "Unknown source instance",
        sourceClass: game === "rac1" ? 0x101 : 0x202,
        transform: {
          position: { x: offsetX + 3, y: 0.6, z: 0 },
          rotationEuler: { x: 0, y: 0, z: 0 },
          scale: { x: 1, y: 1, z: 1 },
        },
        properties: { evidenceStatus: "uninterpreted" },
        source: source(game, levelId, "instance"),
      },
    ],
    splines: [],
    volumes: [],
    spawnPoints: [
      {
        id: `${game}-debug-spawn`,
        kind: "debug-player",
        transform: {
          position: { x: offsetX - 3, y: 0.25, z: 0 },
          rotationEuler: { x: 0, y: 0, z: 0 },
          scale: { x: 1, y: 1, z: 1 },
        },
        source: source(game, levelId, "synthetic-spawn"),
      },
    ],
  };
}

function translate(values: readonly number[], x: number, y: number, z: number): number[] {
  const out: number[] = [];
  for (let i = 0; i < values.length; i += 3) {
    out.push((values[i] ?? 0) + x, (values[i + 1] ?? 0) + y, (values[i + 2] ?? 0) + z);
  }
  return out;
}

export const rac1SyntheticWorld = makeWorld("rac1", -8);
export const rac2SyntheticWorld = makeWorld("rac2", 8);
export const syntheticWorlds = [rac1SyntheticWorld, rac2SyntheticWorld] as const;
