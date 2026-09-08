import type { OBPModel, OBPMesh } from "../../core/src/index.js";
import type { Rac1MobyBindPoseClasses } from "../../rac1-moby/src/index.js";

/**
 * Convert decoded R&C1 Moby class-local bind/rest-pose surfaces into reusable
 * neutral OBP model assets. Native class-local Z-up positions are basis-swapped
 * to OBP Y-up here; placement transform is deliberately left to OBPInstance.
 */
export function buildRac1MobyModels(
  classes: Rac1MobyBindPoseClasses,
  buildId: string,
  levelId: string,
  materialPrefix: string,
  materialIds: ReadonlySet<string>,
): { readonly models: OBPModel[]; readonly modelIdsByClass: ReadonlyMap<number, string> } {
  const models: OBPModel[] = [];
  const modelIdsByClass = new Map<number, string>();
  const animated = new Set(classes.animatedClassIds);

  for (const [oClass, cls] of [...classes.classes.entries()].sort((a, b) => a[0] - b[0])) {
    const mesh = cls.mesh;
    if (mesh.indices.length === 0) continue;
    if (cls.triangleTextureIds.length !== mesh.indices.length / 3) {
      throw new Error(`R&C1 Moby class ${oClass} has inconsistent triangle texture mapping.`);
    }

    interface Group { positions: number[]; uvs: number[]; indices: number[]; remap: Map<number, number> }
    const groups = new Map<number, Group>();
    for (let face = 0; face < cls.triangleTextureIds.length; face++) {
      const textureId = cls.triangleTextureIds[face]!;
      let group = groups.get(textureId);
      if (!group) {
        group = { positions: [], uvs: [], indices: [], remap: new Map() };
        groups.set(textureId, group);
      }
      for (let corner = 0; corner < 3; corner++) {
        const nativeVertex = mesh.indices[face * 3 + corner]!;
        let modelVertex = group.remap.get(nativeVertex);
        if (modelVertex === undefined) {
          modelVertex = group.positions.length / 3;
          group.remap.set(nativeVertex, modelVertex);
          const x = mesh.positions[nativeVertex * 3]!;
          const y = mesh.positions[nativeVertex * 3 + 1]!;
          const z = mesh.positions[nativeVertex * 3 + 2]!;
          // Native RC Z-up -> neutral OBP Y-up, class-local basis only.
          group.positions.push(x, z, y);
          group.uvs.push(mesh.uvs[nativeVertex * 2]!, mesh.uvs[nativeVertex * 2 + 1]!);
        }
        group.indices.push(modelVertex);
      }
    }

    const modelId = `rac1-${levelId}-moby-class${oClass}`;
    const modelMeshes: OBPMesh[] = [];
    for (const [textureId, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
      const materialId = textureId >= 0 ? `${materialPrefix}-tex${textureId}` : undefined;
      if (materialId && !materialIds.has(materialId)) {
        throw new Error(`R&C1 Moby class ${oClass} references missing native texture material '${materialId}'.`);
      }
      modelMeshes.push({
        id: `${modelId}-tex${textureId}`,
        name: `R&C1 Moby class ${oClass} texture ${textureId}`,
        geometry: { positions: group.positions, indices: group.indices, uvs: group.uvs },
        ...(materialId ? { materialId } : {}),
        source: {
          game: "rac1",
          buildId,
          levelId,
          assetKind: "moby-model-mesh",
          originalId: `${oClass}:${textureId}`,
          originalOffset: cls.assetOffset,
          notes: [
            `native oClass ${oClass}`,
            animated.has(oClass)
              ? "animated native class represented by its retail-validated bind/rest-pose surface; animation is not synthesized"
              : "rigid native class bind/rest-pose surface",
            "class-local native Z-up coordinates converted to OBP Y-up; no placement transform baked into this asset",
          ],
        },
      });
    }

    models.push({
      id: modelId,
      name: `R&C1 Moby class ${oClass}`,
      meshes: modelMeshes,
      source: {
        game: "rac1",
        buildId,
        levelId,
        assetKind: "moby-class-model",
        originalId: oClass,
        originalOffset: cls.assetOffset,
        notes: [
          `${mesh.highLodPacketCount} native high-LOD packets`,
          `${mesh.indices.length / 3} bind/rest-pose triangles`,
          animated.has(oClass) ? "native class has joints; model is bind/rest pose only" : "native class is rigid",
          "visual model relation does not assign gameplay semantics beyond preserved sourceClass",
        ],
      },
    });
    modelIdsByClass.set(oClass, modelId);
  }

  return { models, modelIdsByClass };
}
