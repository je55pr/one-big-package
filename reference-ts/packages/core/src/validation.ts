import type { OBPBounds, OBPGeometry, OBPWorld, OBPWorldStats, Vec3 } from "./types.js";
import { OBP_SCHEMA_VERSION } from "./types.js";

export interface ValidationIssue {
  path: string;
  message: string;
}

function finiteVec3(value: Vec3): boolean {
  return Number.isFinite(value.x) && Number.isFinite(value.y) && Number.isFinite(value.z);
}

function validateBounds(bounds: OBPBounds, path: string, issues: ValidationIssue[]): void {
  if (!finiteVec3(bounds.min) || !finiteVec3(bounds.max)) {
    issues.push({ path, message: "bounds must contain finite numbers" });
    return;
  }
  if (bounds.min.x > bounds.max.x || bounds.min.y > bounds.max.y || bounds.min.z > bounds.max.z) {
    issues.push({ path, message: "bounds min must not exceed max" });
  }
}

function validateGeometry(geometry: OBPGeometry, path: string, issues: ValidationIssue[]): void {
  if (geometry.positions.length % 3 !== 0) {
    issues.push({ path: `${path}.positions`, message: "position array length must be divisible by 3" });
  }
  if (geometry.indices.length % 3 !== 0) {
    issues.push({ path: `${path}.indices`, message: "index array length must be divisible by 3" });
  }
  const vertexCount = geometry.positions.length / 3;
  for (let i = 0; i < geometry.positions.length; i++) {
    if (!Number.isFinite(geometry.positions[i])) {
      issues.push({ path: `${path}.positions[${i}]`, message: "position must be finite" });
    }
  }
  for (let i = 0; i < geometry.indices.length; i++) {
    const index = geometry.indices[i];
    if (!Number.isInteger(index) || index === undefined || index < 0 || index >= vertexCount) {
      issues.push({ path: `${path}.indices[${i}]`, message: `index must reference 0..${Math.max(0, vertexCount - 1)}` });
    }
  }
  if (geometry.colors && geometry.colors.length !== geometry.positions.length) {
    issues.push({ path: `${path}.colors`, message: "colors array length must equal positions array length" });
  }
  if (geometry.uvs && geometry.uvs.length !== vertexCount * 2) {
    issues.push({ path: `${path}.uvs`, message: "uvs array length must be 2 per vertex" });
  }
  if (geometry.alpha && geometry.alpha.length !== vertexCount) {
    issues.push({ path: `${path}.alpha`, message: "alpha array length must be 1 per vertex" });
  }
}

export function validateWorld(world: OBPWorld): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (world.schemaVersion !== OBP_SCHEMA_VERSION) {
    issues.push({ path: "schemaVersion", message: `expected ${OBP_SCHEMA_VERSION}` });
  }
  if (!world.id.trim()) issues.push({ path: "id", message: "world id is required" });
  validateBounds(world.bounds, "bounds", issues);

  const ids = new Set<string>();
  const checkId = (id: string, path: string): void => {
    if (!id.trim()) issues.push({ path, message: "id is required" });
    if (ids.has(id)) issues.push({ path, message: `duplicate world-scoped id '${id}'` });
    ids.add(id);
  };

  for (const [i, mesh] of world.meshes.entries()) {
    checkId(mesh.id, `meshes[${i}].id`);
    validateGeometry(mesh.geometry, `meshes[${i}].geometry`, issues);
  }

  const modelIds = new Set<string>();
  for (const [i, model] of (world.models ?? []).entries()) {
    checkId(model.id, `models[${i}].id`);
    modelIds.add(model.id);
    for (const [j, mesh] of model.meshes.entries()) {
      checkId(mesh.id, `models[${i}].meshes[${j}].id`);
      validateGeometry(mesh.geometry, `models[${i}].meshes[${j}].geometry`, issues);
    }
  }

  for (const [i, mesh] of world.collisionMeshes.entries()) {
    checkId(mesh.id, `collisionMeshes[${i}].id`);
    validateGeometry(mesh.geometry, `collisionMeshes[${i}].geometry`, issues);
    if (mesh.triangleMaterialIds && mesh.triangleMaterialIds.length !== mesh.geometry.indices.length / 3) {
      issues.push({ path: `collisionMeshes[${i}].triangleMaterialIds`, message: "must contain one material id per triangle" });
    }
  }
  for (const [i, instance] of world.instances.entries()) {
    checkId(instance.id, `instances[${i}].id`);
    if (instance.modelId !== undefined && !modelIds.has(instance.modelId)) {
      issues.push({ path: `instances[${i}].modelId`, message: `model '${instance.modelId}' does not exist in world.models` });
    }
  }
  for (const [i, spline] of world.splines.entries()) checkId(spline.id, `splines[${i}].id`);
  for (const [i, volume] of world.volumes.entries()) checkId(volume.id, `volumes[${i}].id`);
  for (const [i, spawn] of world.spawnPoints.entries()) checkId(spawn.id, `spawnPoints[${i}].id`);

  return issues;
}

export function worldStats(world: OBPWorld): OBPWorldStats {
  return {
    meshCount: world.meshes.length,
    renderTriangles: world.meshes.reduce((sum, mesh) => sum + mesh.geometry.indices.length / 3, 0),
    collisionMeshCount: world.collisionMeshes.length,
    collisionTriangles: world.collisionMeshes.reduce((sum, mesh) => sum + mesh.geometry.indices.length / 3, 0),
    instanceCount: world.instances.length,
    splineCount: world.splines.length,
    volumeCount: world.volumes.length,
    spawnPointCount: world.spawnPoints.length,
  };
}
