export const OBP_SCHEMA_VERSION = 1 as const;

export type OBPSourceGame = "rac1" | "rac2" | "rac3" | "deadlocked" | "synthetic";

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

export interface OBPBounds {
  min: Vec3;
  max: Vec3;
}

export interface OBPSourceRef {
  game: OBPSourceGame;
  buildId: string;
  levelId?: string;
  assetKind?: string;
  originalId?: string | number;
  originalOffset?: number;
  notes?: readonly string[];
}

export interface OBPMaterial {
  id: string;
  name: string;
  source?: OBPSourceRef;
  debugRgba?: readonly [number, number, number, number];
  /** Optional diffuse image as a `data:` URI (e.g. a decoded native texture). */
  image?: string;
}

export interface OBPGeometry {
  positions: readonly number[];
  indices: readonly number[];
  /** Optional per-vertex RGB in 0..1, 3 per vertex. Length must be `positions.length`. */
  colors?: readonly number[];
  /** Optional per-vertex UV, 2 per vertex. Length must be `positions.length / 3 * 2`. */
  uvs?: readonly number[];
  /** Optional per-vertex alpha in 0..1, 1 per vertex. Length must be `positions.length / 3`. */
  alpha?: readonly number[];
}

export interface OBPMesh {
  id: string;
  name: string;
  geometry: OBPGeometry;
  materialId?: string;
  source: OBPSourceRef;
}

/**
 * Reusable model-local render geometry. Unlike `OBPWorld.meshes`, these meshes
 * have no world transform of their own and become visible only through an
 * `OBPInstance.modelId` reference. This is a visual-asset relation only; native
 * gameplay/class semantics remain independently preserved on the instance.
 */
export interface OBPModel {
  id: string;
  name: string;
  meshes: readonly OBPMesh[];
  source: OBPSourceRef;
}

export interface OBPCollisionMesh {
  id: string;
  name: string;
  geometry: OBPGeometry;
  triangleMaterialIds?: readonly number[];
  source: OBPSourceRef;
}

export interface OBPTransform {
  position: Vec3;
  /** Radians, column-vector XYZ Euler: points encounter Rx, then Ry, then Rz (`Rz * Ry * Rx`). */
  rotationEuler: Vec3;
  /** Local-axis scale; neutral render composition is `T * Rz * Ry * Rx * S`. */
  scale: Vec3;
}

export interface OBPInstance {
  id: string;
  name: string;
  sourceClass: string | number;
  /** Optional reusable visual asset. This does not replace or reinterpret `sourceClass`. */
  modelId?: string;
  transform: OBPTransform;
  properties: Readonly<Record<string, unknown>>;
  source: OBPSourceRef;
}

export interface OBPSpline {
  id: string;
  points: readonly Vec3[];
  source: OBPSourceRef;
}

export interface OBPVolume {
  id: string;
  kind: string;
  transform: OBPTransform;
  source: OBPSourceRef;
}

export interface OBPSpawnPoint {
  id: string;
  kind: string;
  transform: OBPTransform;
  source: OBPSourceRef;
}

/**
 * Level-wide atmosphere: the values a level records once, not per-object. Colours
 * are RGB(A) in 0..1. Distances/heights are in the source game's world units, in
 * OBP (Y-up) space. Every field is optional — an importer fills in what the
 * source actually provides.
 */
export interface OBPEnvironment {
  /** Base sky colour, RGBA. */
  skyColor?: readonly [number, number, number, number];
  /** Screen clear / background colour, RGB. */
  backgroundColor?: readonly [number, number, number];
  fogColor?: readonly [number, number, number];
  fogNearDistance?: number;
  fogFarDistance?: number;
  fogNearIntensity?: number;
  fogFarIntensity?: number;
  /** The kill-plane height (OBP Y). Below this the player dies. */
  deathHeight?: number;
  isSphericalWorld?: boolean;
  /** Planet centre for spherical-gravity levels (OBP coords). */
  sphereCenter?: Vec3;
  source?: OBPSourceRef;
}

export interface OBPWorld {
  schemaVersion: typeof OBP_SCHEMA_VERSION;
  id: string;
  displayName: string;
  source: OBPSourceRef;
  bounds: OBPBounds;
  materials: readonly OBPMaterial[];
  /** Already world-space render geometry. */
  meshes: readonly OBPMesh[];
  /** Optional reusable model-local visual assets referenced by instances. */
  models?: readonly OBPModel[];
  collisionMeshes: readonly OBPCollisionMesh[];
  instances: readonly OBPInstance[];
  splines: readonly OBPSpline[];
  volumes: readonly OBPVolume[];
  spawnPoints: readonly OBPSpawnPoint[];
  environment?: OBPEnvironment;
}

export interface OBPWorldStats {
  meshCount: number;
  renderTriangles: number;
  collisionMeshCount: number;
  collisionTriangles: number;
  instanceCount: number;
  splineCount: number;
  volumeCount: number;
  spawnPointCount: number;
}
