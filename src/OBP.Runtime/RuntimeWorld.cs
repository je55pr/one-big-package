using OBP.Core.Math;

namespace OBP.Runtime;

/// <summary>
/// The engine-independent, game-independent world model — the hand-off point
/// between a game-specific importer (Going Commando today; R&amp;C1 / Up Your
/// Arsenal later) and the Godot world builder. Nothing in here is Ratchet- or
/// Godot-specific: it is welded per-material render geometry, decoded textures,
/// triangle-soup collision, an atmosphere description and a spawn point, all in
/// OBP world space (Y-up; native PS2 Z-up is converted at the importer boundary).
///
/// <para>
/// The importer is what makes a level "recognisable"; this record is just the
/// pipe. Keeping it neutral is the cross-game architecture rule — GC-specific
/// data conversion terminates here, before <c>OBP.Godot</c>.
/// </para>
/// </summary>
public sealed record RuntimeWorld(
    string Game,
    string BuildId,
    int LevelId,
    string? PlanetName,
    string? LocationName,
    IReadOnlyList<RuntimeMesh> Meshes,
    IReadOnlyList<RuntimeTexture> Textures,
    int MaterialCount,
    IReadOnlyList<RuntimeCollisionBlob> CollisionMeshes,
    ObpBounds Bounds,
    RuntimeEnvironment? Environment,
    RuntimeSpawn? Ship,
    RuntimeLighting? Lighting = null,
    IReadOnlyList<RuntimeAnimatedMesh>? AnimatedMeshes = null,
    IReadOnlyList<RuntimeDynamicObject>? DynamicObjects = null)
{
    public int TotalRenderTriangles => Meshes.Sum(m => m.TriangleCount);

    public int TotalDynamicTriangles => (DynamicObjects ?? Array.Empty<RuntimeDynamicObject>())
        .Sum(o => o.Meshes.Sum(m => m.TriangleCount));

    public int TotalCollisionTriangles => CollisionMeshes.Sum(c => c.Triangles);

    /// <summary>A short human label — "Endako — Megapolis", or "LEVEL14" when unnamed.</summary>
    public string DisplayName => PlanetName is { Length: > 0 } p
        ? LocationName is { Length: > 0 } l ? $"{p} — {l}" : p
        : $"LEVEL{LevelId}";
}

/// <summary>
/// One welded, per-material render mesh. Positions are flat XYZ (OBP Y-up);
/// <see cref="Indices"/> is 3 per triangle. <see cref="Colors"/>, when present, is
/// flat RGBA (0..1) per vertex — currently only the sky shells carry it, to fade
/// cloud-layer edges.
/// </summary>
public sealed record RuntimeMesh(string AssetKind, int TextureId, double[] Positions, float[] Uvs, int[] Indices, float[]? Colors = null)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>A decoded RGBA texture, keyed by the same <c>(AssetKind, TextureId)</c> a <see cref="RuntimeMesh"/> carries.</summary>
public sealed record RuntimeTexture(string AssetKind, int TextureId, int Width, int Height, byte[] Rgba);

/// <summary>
/// One placed object whose geometry changes per animation frame — a single
/// instance kept out of the merged <see cref="RuntimeWorld.Meshes"/> soup so the
/// host can swap its vertex buffer over time. <see cref="Frames"/> each hold flat
/// XYZ vertex positions (OBP Y-up world space), already posed and instance-placed;
/// <see cref="Uvs"/> / <see cref="Indices"/> / <see cref="Colors"/> are shared
/// across frames. <see cref="TextureKey"/> resolves against
/// <see cref="RuntimeWorld.Textures"/>.
/// </summary>
public sealed record RuntimeAnimatedMesh(
    string Name,
    string AssetKind,
    int TextureId,
    float[] Uvs,
    int[] Indices,
    float[] Colors,
    IReadOnlyList<double[]> Frames,
    float FramesPerSecond)
{
    public int VertexCount => Frames.Count > 0 ? Frames[0].Length / 3 : 0;

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// A game-neutral placed-object transform. <see cref="Matrix"/> is a column-major
/// 4x4 matrix in OBP Y-up space. Keeping the full matrix avoids baking any one
/// game's Euler convention into the shared runtime contract.
/// </summary>
public sealed record RuntimeObjectTransform(double[] Matrix);

/// <summary>
/// One local-space render surface owned by a <see cref="RuntimeDynamicObject"/>.
/// Positions are OBP Y-up local coordinates; the object's transform places them
/// into the world. This intentionally stays separate from the welded world mesh
/// soup so one native instance can later change state, animate or disappear.
/// </summary>
public sealed record RuntimeObjectMesh(
    string AssetKind,
    int TextureId,
    double[] Positions,
    float[] Uvs,
    int[] Indices,
    float[]? Colors = null)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// Opaque game-owned bytes retained across the neutral runtime boundary. The
/// shared runtime does not interpret <see cref="Format"/> or mutate the payload;
/// the source-game runtime owns those semantics.
/// </summary>
public sealed record RuntimeOpaquePayload(string Format, byte[] Data);

/// <summary>
/// One native gameplay object preserved as an individual runtime entity rather
/// than flattened into static geometry. Identity, transform, render model,
/// interaction identity and opaque source-game state survive the importer
/// boundary while gameplay interpretation remains game-specific.
/// </summary>
public sealed record RuntimeDynamicObject(
    string SourceGame,
    int NativeClassId,
    int InstanceIndex,
    int? NativeUid,
    string ModelRef,
    string InteractionId,
    RuntimeObjectTransform Transform,
    IReadOnlyList<RuntimeObjectMesh> Meshes,
    IReadOnlyList<RuntimeOpaquePayload>? NativePayloads = null);

/// <summary>One decoded collision blob: OBP Y-up flat-XYZ positions, 3 indices per triangle, plus the native face type per triangle.</summary>
public sealed record RuntimeCollisionBlob(int Octants, double[] Positions, int[] Indices, int[] TriangleMaterialIds)
{
    public int Triangles => Indices.Length / 3;
    public int VertexCount => Positions.Length / 3;
}

/// <summary>
/// Level atmosphere: kill plane, spherical-gravity flag, background colour,
/// scene ambient colour, and fog (colour, <b>world-unit</b> near/far distances,
/// 0..255 near/far visibility intensities). The importer resolves the fog and
/// ambient from the nearest environment sample point where the level has them,
/// falling back to the global level settings.
/// </summary>
public sealed record RuntimeEnvironment(
    float DeathHeight,
    bool IsSphericalWorld,
    (double R, double G, double B)? BackgroundColour,
    (double R, double G, double B)? FogColour,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity = 255f,
    float FogFarIntensity = 255f,
    (double R, double G, double B)? AmbientColour = null)
{
    /// <summary>Visibility at the far fog plane, 0 (opaque) .. 1 (clear). Retail intensities are 0..255.</summary>
    public float FogFarVisibility => System.Math.Clamp(FogFarIntensity / 255f, 0f, 1f);
}

/// <summary>Where the player enters — the native ship park point. OBP Y-up; <see cref="Yaw"/> is radians about +Y.</summary>
public sealed record RuntimeSpawn(double X, double Y, double Z, double Yaw);

// --- dynamic lighting (queried per-frame by the host, e.g. for the player) ---

/// <summary>Fog: colour + world-unit near/far distances + 0..255 near/far visibility intensities.</summary>
public sealed record RuntimeFog((double R, double G, double B) Colour, double NearDistance, double FarDistance, double NearIntensity, double FarIntensity);

/// <summary>A directional light — key + fill component, each an RGB colour and a unit direction the light travels (OBP Y-up).</summary>
public sealed record RuntimeDirLight(
    (double R, double G, double B) ColourA, (double X, double Y, double Z) DirectionA,
    (double R, double G, double B) ColourB, (double X, double Y, double Z) DirectionB);

/// <summary>An environment probe: the nearest one to a point sets its ambient ("hero") colour, directional light and fog.</summary>
public sealed record RuntimeEnvSample((double X, double Y, double Z) Position, int HeroLightIndex, (double R, double G, double B) HeroColour, RuntimeFog? Fog);

/// <summary>One end of a <see cref="RuntimeEnvTransition"/>.</summary>
public sealed record RuntimeEnvState((double R, double G, double B) HeroColour, int HeroLightIndex, RuntimeFog Fog);

/// <summary>
/// A box volume across which the hero lighting / fog blend from <see cref="A"/>
/// to <see cref="B"/> (a doorway). <see cref="InverseMatrixZUp"/> maps a native
/// Z-up world point to box-local; the host swaps a Y-up query point's y/z first.
/// <see cref="BoundingSphere"/> is OBP Y-up.
/// </summary>
public sealed record RuntimeEnvTransition(
    double[] InverseMatrixZUp,
    (double X, double Y, double Z, double Radius) BoundingSphere,
    bool BlendHero, bool BlendFog,
    RuntimeEnvState A, RuntimeEnvState B);

/// <summary>Everything the host needs to light the player and pick per-region fog / ambient as it moves.</summary>
public sealed record RuntimeLighting(
    IReadOnlyList<RuntimeDirLight> DirLights,
    IReadOnlyList<RuntimeEnvSample> EnvSamples,
    IReadOnlyList<RuntimeEnvTransition> EnvTransitions);
