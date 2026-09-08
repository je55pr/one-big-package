using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.RAC2.Geometry;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.RAC2;

/// <summary>
/// Native world assembly for a Going Commando retail level — the C# equivalent of
/// <c>reference-ts/tools/gc-world.mjs</c>. Opens the level WAD from an ISO,
/// decodes chunk-0 tfrags + octree collision, instances the tie / shrub / moby
/// classes (per-texture grouping + vertex welding, native Z-up → OBP Y-up), adds
/// moby instance markers, the sky shells and a death-height plane, and grows the
/// world bounds.
///
/// <para>
/// Output is a neutral <see cref="RuntimeWorld"/> — no GC or Godot types cross
/// this boundary. This is where GC-specific data conversion terminates.
/// </para>
/// </summary>
public static class GcWorldImport
{
    private static double R2(double v) => System.Math.Floor(v * 100 + 0.5) / 100 + 0.0;

    private static double R4(double v) => System.Math.Floor(v * 10000 + 0.5) / 10000 + 0.0;

    /// <summary>Import a Going Commando level straight from a retail ISO into the neutral runtime world model.</summary>
    public static RuntimeWorld Build(IRandomAccessReader isoReader, int level)
    {
        var fs = Iso9660Filesystem.Open(isoReader);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD") ?? throw new FileNotFoundException($"/G/LEVEL{level}.WAD");
        var header = GcLevelWad.ReadHeader(wad);
        var chunkSlots = new[] { 4, 5, 6 }.Where(s => header.Lumps[s].Present).ToArray();
        bool chunked = chunkSlots.Length > 0;

        var meshes = new List<RuntimeMesh>();
        var textures = new List<RuntimeTexture>();
        int materialCount = 0;
        var collision = new List<RuntimeCollisionBlob>();

        void CollectTextures(string kind, GcLevelTextures.Table table)
        {
            var decoded = GcLevelTextures.Read(Core(), table);
            materialCount += decoded.Count;
            foreach (var t in decoded)
            {
                textures.Add(new RuntimeTexture(kind, t.Index, t.Width, t.Height, t.Rgba));
            }
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        void Grow(double x, double y, double z)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        // Trusted extent — tfrags + octree collision only. Some levels (LEVEL14)
        // place tie instances thousands of units off the level; those still grow
        // `world.Bounds`, but the sky scale, death-plane size and any camera
        // framing derive from this so one stray instance can't inflate them.
        double tMinX = double.PositiveInfinity, tMinY = double.PositiveInfinity, tMinZ = double.PositiveInfinity;
        double tMaxX = double.NegativeInfinity, tMaxY = double.NegativeInfinity, tMaxZ = double.NegativeInfinity;
        void GrowTrusted(double x, double y, double z)
        {
            if (x < tMinX) tMinX = x;
            if (y < tMinY) tMinY = y;
            if (z < tMinZ) tMinZ = z;
            if (x > tMaxX) tMaxX = x;
            if (y > tMaxY) tMaxY = y;
            if (z > tMaxZ) tMaxZ = z;
        }

        GcLevelCore.Core? coreCache = null;
        GcLevelCore.Core Core() => coreCache ??= GcLevelCore.Open(GcLevelWad.RequireLump(wad, header, 0));

        // --- tfrag textures (materials) ---
        CollectTextures("tfrag", GcLevelTextures.Table.Tfrag);

        // --- tfrags ---
        // Chunked levels: each present chunk slot is a spatial tile of the
        // terrain (often disjoint regions AND altitudes — Grelbin's chunks are
        // stacked vertically), so decode every one, exactly as the collision
        // loop below does. (Chunk 0 alone is only the region around the ship.)
        // Un-chunked levels: the tfrag section of the level core.
        var tfragBlobs = new List<byte[]>();
        if (chunked)
        {
            foreach (int slot in chunkSlots)
            {
                var chunk = GcLevelWad.OpenLump(wad, header, slot);
                if (chunk is null)
                {
                    continue;
                }

                int tfragOffset = BinaryPrimitives.ReadInt32LittleEndian(chunk.Read(0, 8).AsSpan());
                if (tfragOffset > 0)
                {
                    tfragBlobs.Add(WadLz.ReadBlock(chunk, tfragOffset).Data);
                }
            }
        }
        else
        {
            var core = Core();
            var range = GcLevelCore.SectionRange(core, core.Header.Tfrags);
            if (range is null && core.Header.Tfrags == 0)
            {
                int next = core.SectionBoundaries.FirstOrDefault(b => b > 0, 0);
                if (next > 0)
                {
                    range = new GcLevelCore.ByteRange(0, next);
                }
            }

            if (range is { } r && r.Size > 0 && r.Offset + r.Size <= core.Assets.Length)
            {
                tfragBlobs.Add(core.Assets.AsSpan(r.Offset, r.Size).ToArray());
            }
        }

        foreach (var tfragBlob in tfragBlobs)
        {
            var tf = GcTfrag.Read(tfragBlob);
            foreach (var m in ToTfragMeshes(tf))
            {
                meshes.Add(m);
            }

            Grow(tf.BoundsMin.X, tf.BoundsMin.Y, tf.BoundsMin.Z);
            Grow(tf.BoundsMax.X, tf.BoundsMax.Y, tf.BoundsMax.Z);
            GrowTrusted(tf.BoundsMin.X, tf.BoundsMin.Y, tf.BoundsMin.Z);
            GrowTrusted(tf.BoundsMax.X, tf.BoundsMax.Y, tf.BoundsMax.Z);
        }

        // --- tie / shrub / moby instances ---
        var gameplay = GcInstances.Read(GcLevelWad.RequireLump(wad, header, 2));
        var mobyClasses = GcMoby.ReadClasses(Core());

        CollectTextures("tie", GcLevelTextures.Table.Tie);
        var tieClasses = GcTie.ReadClasses(Core());
        PlaceMatrixInstances("tie", gameplay.TieInstances,
            id => tieClasses.TryGetValue(id, out var c) ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds) : null,
            meshes, Grow, growBounds: true);

        CollectTextures("shrub", GcLevelTextures.Table.Shrub);
        var shrubClasses = GcShrub.ReadClasses(Core());
        PlaceMatrixInstances("shrub", gameplay.ShrubInstances,
            id => shrubClasses.TryGetValue(id, out var c) ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds) : null,
            meshes, Grow, growBounds: true);

        CollectTextures("moby", GcLevelTextures.Table.Moby);
        var animatedMeshes = BuildAnimatedMeshes(gameplay, mobyClasses, out var animatedInstances);
        var dynamicObjects = BuildDynamicMobyObjects(
            level,
            gameplay,
            mobyClasses,
            gameplay.DirLights,
            gameplay.PointLights,
            dynamicClasses: new HashSet<int> { 500 },
            out var dynamicInstances);
        var nonStaticMobyInstances = animatedInstances.Concat(dynamicInstances).ToHashSet();
        PlaceMobyInstances(gameplay.MobyInstances, mobyClasses, gameplay.DirLights, gameplay.PointLights, meshes, nonStaticMobyInstances);

        // --- moby markers ---
        AddMobyMarkers(gameplay.MobyInstances, mobyClasses, meshes, minX, minY, minZ, maxX, maxY, maxZ);

        // --- collision: chunk lumps for chunked planets, the level core for the rest ---
        void AddCollision(RcCollision.Mesh mesh)
        {
            // Native Z-up -> OBP Y-up, same rule as every other world position.
            var positions = new double[mesh.Positions.Length];
            for (int i = 0; i + 3 <= mesh.Positions.Length; i += 3)
            {
                positions[i] = mesh.Positions[i];
                positions[i + 1] = mesh.Positions[i + 2];
                positions[i + 2] = mesh.Positions[i + 1];
            }

            var indices = new int[mesh.Triangles.Count * 3];
            var materialIds = new int[mesh.Triangles.Count];
            for (int t = 0; t < mesh.Triangles.Count; t++)
            {
                var tri = mesh.Triangles[t];
                indices[t * 3] = tri.A;
                indices[t * 3 + 1] = tri.B;
                indices[t * 3 + 2] = tri.C;
                materialIds[t] = tri.MaterialId;
            }

            collision.Add(new RuntimeCollisionBlob(mesh.Octants.Count, positions, indices, materialIds));
            Grow(mesh.Bounds.Min.X, mesh.Bounds.Min.Z, mesh.Bounds.Min.Y);
            Grow(mesh.Bounds.Max.X, mesh.Bounds.Max.Z, mesh.Bounds.Max.Y);
            GrowTrusted(mesh.Bounds.Min.X, mesh.Bounds.Min.Z, mesh.Bounds.Min.Y);
            GrowTrusted(mesh.Bounds.Max.X, mesh.Bounds.Max.Z, mesh.Bounds.Max.Y);
        }

        if (chunked)
        {
            foreach (int slot in chunkSlots)
            {
                var chunk = GcLevelWad.OpenLump(wad, header, slot);
                if (chunk is null)
                {
                    continue;
                }

                int collOffset = BinaryPrimitives.ReadInt32LittleEndian(chunk.Read(0, 8).AsSpan(4));
                if (collOffset <= 0)
                {
                    continue;
                }

                AddCollision(RcCollision.Read(WadLz.ReadBlock(chunk, collOffset).Data));
            }
        }
        else
        {
            var coll = GcLevelCore.CollisionSection(Core());
            if (!coll.IsEmpty)
            {
                AddCollision(RcCollision.Read(coll.ToArray()));
            }
        }

        // --- environment (level settings + nearest env sample point) ---
        RuntimeEnvironment? environment = null;
        RuntimeSpawn? ship = null;
        try
        {
            var s = GcLevelSettings.Read(GcLevelWad.RequireLump(wad, header, 2));

            // The level-settings fog distances are fixed-point; the TS viewer's
            // scale (÷1024) has been visually calibrated. Bring them to world
            // units here so RuntimeEnvironment always carries world units.
            const float settingsFogScale = 1f / 1024f;
            (double R, double G, double B)? fogColour = s.FogColour;
            float fogNear = s.FogNearDistance * settingsFogScale;
            float fogFar = s.FogFarDistance * settingsFogScale;
            float fogNearI = s.FogNearIntensity;
            float fogFarI = s.FogFarIntensity;
            (double R, double G, double B)? ambient = null;

            // Environment sample points are per-region atmosphere probes. Use the
            // one nearest the ship park point for the ambient ("hero") colour,
            // and — separately, since the spawn region often defines no fog while
            // the open areas do — the nearest sample that carries a fog override
            // for the fog (in world units).
            var refPoint = s.ShipPosition;
            double Near(GcInstances.EnvSample es)
            {
                double dx = es.Position.X - refPoint.X, dy = es.Position.Y - refPoint.Y, dz = es.Position.Z - refPoint.Z;
                return dx * dx + dy * dy + dz * dz;
            }

            var nearest = gameplay.EnvSamples.OrderBy(Near).FirstOrDefault();
            var nearestWithFog = gameplay.EnvSamples.Where(es => es.Fog is not null).OrderBy(Near).FirstOrDefault();

            if (nearest is { } n)
            {
                ambient = (n.HeroColour.R, n.HeroColour.G, n.HeroColour.B);
            }

            if (nearestWithFog?.Fog is { } ef)
            {
                fogColour = (ef.Colour.R, ef.Colour.G, ef.Colour.B);
                fogNear = ef.NearDistance;
                fogFar = ef.FarDistance;
                fogNearI = ef.NearIntensity;
                fogFarI = ef.FarIntensity;
            }

            environment = new RuntimeEnvironment(
                s.DeathHeight, s.IsSphericalWorld, s.BackgroundColour, fogColour,
                fogNear, fogFar, fogNearI, fogFarI, ambient);

            // Native Z-up -> OBP Y-up; native rotation about +Z becomes yaw about +Y.
            ship = new RuntimeSpawn(s.ShipPosition.X, s.ShipPosition.Z, s.ShipPosition.Y, s.ShipRotationZ);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            environment = null;
        }

        bool haveBounds = !double.IsPositiveInfinity(minX);

        // Centre + radius for the sky dome and death-plane come from the trusted
        // extent (tfrag + collision) so a stray far-off instance can't blow them
        // up; fall back to the full bounds when there is no trusted geometry.
        bool haveTrusted = !double.IsPositiveInfinity(tMinX);
        double cMinX = haveTrusted ? tMinX : minX, cMinY = haveTrusted ? tMinY : minY, cMinZ = haveTrusted ? tMinZ : minZ;
        double cMaxX = haveTrusted ? tMaxX : maxX, cMaxY = haveTrusted ? tMaxY : maxY, cMaxZ = haveTrusted ? tMaxZ : maxZ;
        var ctr = haveBounds
            ? ((cMinX + cMaxX) / 2, (cMinY + cMaxY) / 2, (cMinZ + cMaxZ) / 2)
            : (0.0, 0.0, 0.0);
        double levelRadius = haveBounds
            ? 0.5 * System.Math.Sqrt(
                (cMaxX - cMinX) * (cMaxX - cMinX) + (cMaxY - cMinY) * (cMaxY - cMinY) + (cMaxZ - cMinZ) * (cMaxZ - cMinZ))
            : 100;

        // --- sky dome ---
        var sky = GcSky.ReadLevelSky(Core());
        if (sky is not null && sky.Shells.Count > 0)
        {
            materialCount += sky.Textures.Count + 1; // sky textures + the gouraud backdrop material
            foreach (var t in sky.Textures)
            {
                textures.Add(new RuntimeTexture("sky", t.Index, t.Width, t.Height, t.Rgba));
            }

            double shellMax = 0;
            foreach (var shell in sky.Shells)
            {
                foreach (double p in shell.Positions)
                {
                    shellMax = System.Math.Max(shellMax, System.Math.Abs(p));
                }
            }

            // Build the shells centred on the origin: the Godot adapter parks
            // the sky node on the camera every frame, so anything baked into the
            // positions here would be double-counted (the "giant offset orb").
            // Make the dome larger than any level geometry so the adapter's
            // depth test puts every building / spire in front of the clouds.
            double domeRadius = System.Math.Max(levelRadius * 3.0, 2000.0);
            double scale = shellMax > 0 ? domeRadius / shellMax : 1;
            foreach (var shell in sky.Shells)
            {
                AddSkyShell(shell, scale, meshes);
            }
        }

        // --- death-height plane ---
        if (environment is { } env && float.IsFinite(env.DeathHeight) && haveBounds)
        {
            double y = env.DeathHeight;
            if (y > cMinY - 6 * levelRadius && y < cMaxY + 2 * levelRadius)
            {
                double mx = 0.15 * (cMaxX - cMinX), mz = 0.15 * (cMaxZ - cMinZ);
                double x0 = R2(cMinX - mx), x1 = R2(cMaxX + mx), z0 = R2(cMinZ - mz), z1 = R2(cMaxZ + mz);
                meshes.Add(new RuntimeMesh("death-plane", -1,
                    [x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1],
                    [],
                    [0, 2, 1, 0, 3, 2]));
                materialCount += 1;
            }
        }

        var bounds = haveBounds
            ? new ObpBounds(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ))
            : new ObpBounds(Vec3.Zero, Vec3.Zero);

        var lighting = BuildLighting(gameplay);

        var entry = GcPlanetCatalogue.Find(level);
        return new RuntimeWorld(
            Game: "rac2",
            BuildId: Rac2Authority.Primary.BuildId,
            LevelId: level,
            PlanetName: entry?.Planet,
            LocationName: entry?.Location,
            Meshes: meshes,
            Textures: textures,
            MaterialCount: materialCount,
            CollisionMeshes: collision,
            Bounds: bounds,
            Environment: environment,
            Ship: ship,
            Lighting: lighting,
            AnimatedMeshes: animatedMeshes,
            DynamicObjects: dynamicObjects);
    }

    /// <summary>
    /// Build the per-frame geometry for a small, bounded set of animated moby
    /// instances (single-joint classes with a real <see cref="GcMoby.MobySequence"/>),
    /// kept out of the merged static soup so the host can play them over time.
    /// Also reports which instance indices were consumed so the static path can
    /// skip them.
    /// </summary>
    private static List<RuntimeAnimatedMesh> BuildAnimatedMeshes(
        GcInstances.Gameplay gameplay,
        Dictionary<int, GcMoby.MobyClass> classes,
        out HashSet<int> animatedInstances)
    {
        const int MaxAnimatedInstances = 12;
        animatedInstances = [];
        var outp = new List<RuntimeAnimatedMesh>();

        foreach (var inst in gameplay.MobyInstances)
        {
            if (animatedInstances.Count >= MaxAnimatedInstances)
            {
                break;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            // Single-joint rigid classes only: their rest pose is reproduced
            // exactly (identity-frame invariant), so playback is retail-faithful
            // without the partially-pinned multi-joint skeleton risk.
            if (cls.Joints.Count != 1)
            {
                continue;
            }

            var seq = cls.Sequences
                .Where(s => s.Frames.Count >= 8)
                .OrderByDescending(s => s.Frames.Count)
                .FirstOrDefault();
            if (seq is null)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var m = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);

            var frames = new List<double[]>(seq.Frames.Count);
            foreach (var frame in seq.Frames)
            {
                var posed = OBP.RAC2.Animation.MobyAnimation.Pose(cls.Mesh, cls.Joints, frame);
                var world = new double[posed.Length];
                for (int i = 0; i < posed.Length; i += 3)
                {
                    var (x, y, z) = GcInstances.TransformPoint(m, posed[i], posed[i + 1], posed[i + 2]);
                    world[i] = R2(x);
                    world[i + 1] = R2(z);
                    world[i + 2] = R2(y);
                }

                frames.Add(world);
            }

            // Flat per-vertex shade from the instance's baked ambient.
            int vc = cls.Mesh.Positions.Length / 3;
            var colours = new float[vc * 4];
            for (int v = 0; v < vc; v++)
            {
                colours[v * 4] = (float)System.Math.Clamp(inst.LightColour.R, 0.05, 1.0);
                colours[v * 4 + 1] = (float)System.Math.Clamp(inst.LightColour.G, 0.05, 1.0);
                colours[v * 4 + 2] = (float)System.Math.Clamp(inst.LightColour.B, 0.05, 1.0);
                colours[v * 4 + 3] = 1f;
            }

            float speed = seq.Frames.Count > 0 ? seq.Frames[0].Speed : 0.5f;
            float fps = (float)System.Math.Clamp((speed <= 0 ? 0.5 : speed) * 60.0, 6.0, 60.0);

            // One animated mesh per triangle-texture group (mirrors the static
            // moby split); frames are reference-shared across the groups.
            var byTex = new Dictionary<int, List<int>>();
            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                if (!byTex.TryGetValue(tex, out var list))
                {
                    byTex[tex] = list = [];
                }

                list.Add(cls.Mesh.Indices[f * 3]);
                list.Add(cls.Mesh.Indices[f * 3 + 1]);
                list.Add(cls.Mesh.Indices[f * 3 + 2]);
            }

            foreach (var (tex, tris) in byTex.OrderBy(kv => kv.Key))
            {
                outp.Add(new RuntimeAnimatedMesh(
                    Name: $"moby{inst.OClass}_i{inst.Index}_t{tex}",
                    AssetKind: "moby",
                    TextureId: tex,
                    Uvs: cls.Mesh.Uvs,
                    Indices: tris.ToArray(),
                    Colors: colours,
                    Frames: frames,
                    FramesPerSecond: fps));
            }

            animatedInstances.Add(inst.Index);
        }

        return outp;
    }

    /// <summary>
    /// Lift selected native Mobies out of the welded static render soup while
    /// preserving their per-instance identity, transform, render surfaces and
    /// opaque authored state. This is the gameplay bridge: RuntimeWorld stays
    /// game-neutral while RAC2 decides which native classes need individuality.
    /// </summary>
    private static List<RuntimeDynamicObject> BuildDynamicMobyObjects(
        int level,
        GcInstances.Gameplay gameplay,
        Dictionary<int, GcMoby.MobyClass> classes,
        IReadOnlyList<GcInstances.DirLight> dirLights,
        IReadOnlyList<GcInstances.PointLight> pointLights,
        IReadOnlySet<int> dynamicClasses,
        out HashSet<int> dynamicInstances)
    {
        dynamicInstances = [];
        var outp = new List<RuntimeDynamicObject>();
        var plYUp = pointLights
            .Select(p => (Pos: YUp(p.Position), p.Radius, p.Colour))
            .ToArray();

        foreach (var inst in gameplay.MobyInstances)
        {
            if (!dynamicClasses.Contains(inst.OClass))
            {
                continue;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var nativeMatrix = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);
            var obpMatrix = ToObpMatrix(nativeMatrix);

            var amb = inst.LightColour;
            GcInstances.DirLight? dl = inst.LightIndex >= 0 && inst.LightIndex < dirLights.Count
                ? dirLights[inst.LightIndex]
                : null;
            var (dAx, dAy, dAz) = dl is { } a ? YUp(a.DirectionA) : (0, 0, 0);
            var (dBx, dBy, dBz) = dl is { } b ? YUp(b.DirectionB) : (0, 0, 0);
            var instYUp = YUp(inst.Position);
            var nearPl = plYUp
                .Where(p => Dist2(p.Pos, instYUp) < (p.Radius + 40.0) * (p.Radius + 40.0))
                .ToArray();

            var meshData = cls.Mesh;
            bool haveNormals = meshData.Normals.Length == meshData.Positions.Length;
            var localPositions = new double[meshData.Positions.Length];
            var colours = new float[meshData.Positions.Length / 3 * 4];
            for (int i = 0; i < meshData.Positions.Length; i += 3)
            {
                // Class-local native Z-up -> class-local OBP Y-up. The full
                // instance transform remains separate in RuntimeObjectTransform.
                localPositions[i] = R2(meshData.Positions[i]);
                localPositions[i + 1] = R2(meshData.Positions[i + 2]);
                localPositions[i + 2] = R2(meshData.Positions[i + 1]);

                var (x, y, z) = GcInstances.TransformPoint(
                    nativeMatrix, meshData.Positions[i], meshData.Positions[i + 1], meshData.Positions[i + 2]);
                double lr = amb.R, lg = amb.G, lb = amb.B;
                if (haveNormals)
                {
                    var (nx, ny, nz) = WorldNormal(
                        nativeMatrix, meshData.Normals[i], meshData.Normals[i + 1], meshData.Normals[i + 2]);
                    if (dl is { } light)
                    {
                        double ka = System.Math.Max(0.0, -(nx * dAx + ny * dAy + nz * dAz));
                        double kb = System.Math.Max(0.0, -(nx * dBx + ny * dBy + nz * dBz));
                        lr += light.ColourA.X * ka + light.ColourB.X * kb;
                        lg += light.ColourA.Y * ka + light.ColourB.Y * kb;
                        lb += light.ColourA.Z * ka + light.ColourB.Z * kb;
                    }

                    foreach (var p in nearPl)
                    {
                        double vx = x - p.Pos.X, vy = z - p.Pos.Y, vz = y - p.Pos.Z;
                        double dist = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
                        if (dist >= p.Radius || dist < 1e-4)
                        {
                            continue;
                        }

                        double atten = 1.0 - dist / p.Radius;
                        double ndl = System.Math.Max(0.0, -(nx * vx + ny * vy + nz * vz) / dist);
                        double c = atten * atten * ndl;
                        lr += p.Colour.R * c;
                        lg += p.Colour.G * c;
                        lb += p.Colour.B * c;
                    }
                }

                int v = i / 3;
                colours[v * 4] = (float)System.Math.Clamp(lr, 0.03, 1.0);
                colours[v * 4 + 1] = (float)System.Math.Clamp(lg, 0.03, 1.0);
                colours[v * 4 + 2] = (float)System.Math.Clamp(lb, 0.03, 1.0);
                colours[v * 4 + 3] = 1f;
            }

            var byTex = new Dictionary<int, List<int>>();
            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                if (!byTex.TryGetValue(tex, out var tris))
                {
                    byTex[tex] = tris = [];
                }

                tris.Add(meshData.Indices[f * 3]);
                tris.Add(meshData.Indices[f * 3 + 1]);
                tris.Add(meshData.Indices[f * 3 + 2]);
            }

            var surfaces = byTex
                .OrderBy(kv => kv.Key)
                .Select(kv => new RuntimeObjectMesh(
                    "moby", kv.Key, localPositions, meshData.Uvs, kv.Value.ToArray(), colours))
                .ToArray();

            var payloads = new List<RuntimeOpaquePayload>
            {
                new("rac2-moby-instance-0x88", inst.RawInstance),
            };
            if (inst.PVarData is { } pvar)
            {
                payloads.Add(new RuntimeOpaquePayload("rac2-pvar", pvar));
            }

            outp.Add(new RuntimeDynamicObject(
                SourceGame: "rac2",
                NativeClassId: inst.OClass,
                InstanceIndex: inst.Index,
                NativeUid: inst.Uid,
                ModelRef: $"moby:{inst.OClass}",
                InteractionId: $"level:{level}:moby:{inst.Index}",
                Transform: new RuntimeObjectTransform(obpMatrix),
                Meshes: surfaces,
                NativePayloads: payloads));
            dynamicInstances.Add(inst.Index);
        }

        return outp;
    }

    /// <summary>Convert a native Z-up local-to-world matrix to the equivalent OBP Y-up matrix.</summary>
    private static double[] ToObpMatrix(IReadOnlyList<double> native)
    {
        int[] p = [0, 2, 1, 3];
        var outp = new double[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                outp[r + c * 4] = native[p[r] + p[c] * 4];
            }
        }

        return outp;
    }

    /// <summary>Package the gameplay lights into the neutral runtime lighting model (native Z-up → OBP Y-up).</summary>
    private static RuntimeLighting BuildLighting(GcInstances.Gameplay g)
    {
        static (double, double, double) C((float R, float G, float B) c) => (c.R, c.G, c.B);
        static (double, double, double) V((float X, float Y, float Z) v) => (v.X, v.Z, v.Y); // Z-up -> Y-up

        static RuntimeFog Fog(GcInstances.EnvFog f) =>
            new(C(f.Colour), f.NearDistance, f.FarDistance, f.NearIntensity, f.FarIntensity);

        static RuntimeFog StateFog(GcInstances.EnvState s) =>
            new(C(s.FogColour), s.FogNearDistance, s.FogFarDistance, s.FogNearIntensity, s.FogFarIntensity);

        var dir = g.DirLights
            .Select(d => new RuntimeDirLight(C(d.ColourA), V(d.DirectionA), C(d.ColourB), V(d.DirectionB)))
            .ToArray();

        var samples = g.EnvSamples
            .Select(s => new RuntimeEnvSample(V(s.Position), s.HeroLightIndex, C(s.HeroColour), s.Fog is { } f ? Fog(f) : null))
            .ToArray();

        var transitions = g.EnvTransitions
            .Select(t => new RuntimeEnvTransition(
                t.InverseMatrix.Select(x => (double)x).ToArray(),
                (t.BoundingSphere.X, t.BoundingSphere.Z, t.BoundingSphere.Y, t.BoundingSphere.R),
                t.EnableHero, t.EnableFog,
                new RuntimeEnvState(C(t.StateA.HeroColour), t.StateA.HeroLightIndex, StateFog(t.StateA)),
                new RuntimeEnvState(C(t.StateB.HeroColour), t.StateB.HeroLightIndex, StateFog(t.StateB))))
            .ToArray();

        return new RuntimeLighting(dir, samples, transitions);
    }

    private static IEnumerable<RuntimeMesh> ToTfragMeshes(GcTfrag.Mesh mesh)
    {
        var byTex = new Dictionary<int, List<int>>();
        for (int f = 0; f < mesh.TriangleTextureIds.Length; f++)
        {
            int tex = mesh.TriangleTextureIds[f];
            if (!byTex.TryGetValue(tex, out var list))
            {
                byTex[tex] = list = [];
            }

            list.Add(mesh.Indices[f * 3]);
            list.Add(mesh.Indices[f * 3 + 1]);
            list.Add(mesh.Indices[f * 3 + 2]);
        }

        bool haveColours = mesh.Colors.Length == mesh.Positions.Length;
        foreach (var (tex, tris) in byTex.OrderBy(kv => kv.Key))
        {
            var remap = new Dictionary<int, int>();
            var positions = new List<double>();
            var uvs = new List<float>();
            var colours = new List<float>();
            var indices = new List<int>();
            foreach (int v in tris)
            {
                if (!remap.TryGetValue(v, out int nv))
                {
                    nv = positions.Count / 3;
                    remap[v] = nv;
                    positions.Add(mesh.Positions[v * 3]);
                    positions.Add(mesh.Positions[v * 3 + 1]);
                    positions.Add(mesh.Positions[v * 3 + 2]);
                    uvs.Add(mesh.Uvs[v * 2]);
                    uvs.Add(mesh.Uvs[v * 2 + 1]);
                    // Baked tfrag vertex colour (PS2 lighting/AO), faithful /255.
                    // The PS2 GS doubles it at raster time (0x80 == 1.0); the
                    // scene builder applies that 2x when it consumes these.
                    if (haveColours)
                    {
                        colours.Add(mesh.Colors[v * 3]);
                        colours.Add(mesh.Colors[v * 3 + 1]);
                        colours.Add(mesh.Colors[v * 3 + 2]);
                        colours.Add(1f);
                    }
                }

                indices.Add(nv);
            }

            yield return new RuntimeMesh("tfrag", tex, positions.ToArray(), uvs.ToArray(), indices.ToArray(),
                haveColours ? colours.ToArray() : null);
        }
    }

    private static void PlaceMatrixInstances(
        string kind,
        IReadOnlyList<GcInstances.MatrixInstance> instances,
        Func<int, (double[] Positions, float[] Uvs, int[] Indices, int[] TriTexIds)?> lookup,
        List<RuntimeMesh> meshes,
        Action<double, double, double> grow,
        bool growBounds)
    {
        var byTex = new Dictionary<int, (List<double> P, List<float> U, List<int> I, Dictionary<(double, double, double, double, double), int> Weld)>();

        foreach (var inst in instances)
        {
            var cls = lookup(inst.OClass);
            if (cls is not { } c || c.Indices.Length == 0)
            {
                continue;
            }

            var m = inst.Matrix;
            var worldPos = new double[c.Positions.Length];
            for (int i = 0; i < c.Positions.Length; i += 3)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, c.Positions[i], c.Positions[i + 1], c.Positions[i + 2]);
                worldPos[i] = R2(x);
                worldPos[i + 1] = R2(z);
                worldPos[i + 2] = R2(y);
                if (growBounds)
                {
                    grow(x, z, y);
                }
            }

            for (int f = 0; f < c.TriTexIds.Length; f++)
            {
                int tex = c.TriTexIds[f];
                if (!byTex.TryGetValue(tex, out var g))
                {
                    byTex[tex] = g = ([], [], [], new Dictionary<(double, double, double, double, double), int>());
                }

                for (int k = 0; k < 3; k++)
                {
                    int vi = c.Indices[f * 3 + k];
                    double px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
                    float s = c.Uvs[vi * 2], t = c.Uvs[vi * 2 + 1];
                    double rs = R4(s), rt = R4(t);
                    var key = (px, py, pz, rs, rt);
                    if (!g.Weld.TryGetValue(key, out int idx))
                    {
                        idx = g.P.Count / 3;
                        g.Weld[key] = idx;
                        g.P.Add(px);
                        g.P.Add(py);
                        g.P.Add(pz);
                        g.U.Add((float)rs);
                        g.U.Add((float)rt);
                    }

                    g.I.Add(idx);
                }
            }
        }

        foreach (var (tex, g) in byTex.OrderBy(kv => kv.Key))
        {
            meshes.Add(new RuntimeMesh(kind, tex, g.P.ToArray(), g.U.ToArray(), g.I.ToArray()));
        }
    }

    private static void PlaceMobyInstances(
        IReadOnlyList<GcInstances.MobyInstance> instances,
        Dictionary<int, GcMoby.MobyClass> classes,
        IReadOnlyList<GcInstances.DirLight> dirLights,
        IReadOnlyList<GcInstances.PointLight> pointLights,
        List<RuntimeMesh> meshes,
        HashSet<int> skipInstances)
    {
        var byTex = new Dictionary<int, (List<double> P, List<float> U, List<float> C, List<int> I, Dictionary<(double, double, double, double, double, double, double, double), int> Weld)>();

        // Point lights in OBP Y-up space (they are stored native Z-up).
        var plYUp = pointLights
            .Select(p => (Pos: YUp(p.Position), p.Radius, p.Colour))
            .ToArray();

        foreach (var inst in instances)
        {
            if (skipInstances.Contains(inst.Index))
            {
                continue;
            }

            if (!classes.TryGetValue(inst.OClass, out var cls) || cls.Mesh.Indices.Length == 0)
            {
                continue;
            }

            double sc = inst.Scale == 0 ? 1 : inst.Scale;
            var m = GcInstances.MatrixFromPosRotScale(
                (inst.Position.X, inst.Position.Y, inst.Position.Z),
                (inst.Rotation.X, inst.Rotation.Y, inst.Rotation.Z), sc);

            // The instance's static ambient, plus the one directional light it
            // references (native Z-up dirs -> OBP Y-up). See GcInstances.DirLight.
            var amb = inst.LightColour;
            GcInstances.DirLight? dl = inst.LightIndex >= 0 && inst.LightIndex < dirLights.Count
                ? dirLights[inst.LightIndex]
                : null;
            var (dAx, dAy, dAz) = dl is { } a ? YUp(a.DirectionA) : (0, 0, 0);
            var (dBx, dBy, dBz) = dl is { } b ? YUp(b.DirectionB) : (0, 0, 0);

            // Point lights whose sphere plausibly reaches this instance (only
            // mobies are point-lit). Instance origin in OBP Y-up.
            var instYUp = YUp(inst.Position);
            var nearPl = plYUp
                .Where(p => Dist2(p.Pos, instYUp) < (p.Radius + 40.0) * (p.Radius + 40.0))
                .ToArray();

            var meshData = cls.Mesh;
            bool haveNormals = meshData.Normals.Length == meshData.Positions.Length;
            var worldPos = new double[meshData.Positions.Length];
            var litR = new float[meshData.Positions.Length / 3];
            var litG = new float[meshData.Positions.Length / 3];
            var litB = new float[meshData.Positions.Length / 3];
            for (int i = 0; i < meshData.Positions.Length; i += 3)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, meshData.Positions[i], meshData.Positions[i + 1], meshData.Positions[i + 2]);
                worldPos[i] = R2(x);
                worldPos[i + 1] = R2(z);
                worldPos[i + 2] = R2(y);

                double lr = amb.R, lg = amb.G, lb = amb.B;
                if (haveNormals)
                {
                    var (nx, ny, nz) = WorldNormal(m, meshData.Normals[i], meshData.Normals[i + 1], meshData.Normals[i + 2]);
                    if (dl is { } light)
                    {
                        // surface faces -direction to be lit (light-travel convention)
                        double ka = System.Math.Max(0.0, -(nx * dAx + ny * dAy + nz * dAz));
                        double kb = System.Math.Max(0.0, -(nx * dBx + ny * dBy + nz * dBz));
                        lr += light.ColourA.X * ka + light.ColourB.X * kb;
                        lg += light.ColourA.Y * ka + light.ColourB.Y * kb;
                        lb += light.ColourA.Z * ka + light.ColourB.Z * kb;
                    }

                    foreach (var p in nearPl)
                    {
                        double vx = x - p.Pos.X, vy = z - p.Pos.Y, vz = y - p.Pos.Z; // vertex is (x,z,y) in Y-up
                        double dist = System.Math.Sqrt(vx * vx + vy * vy + vz * vz);
                        if (dist >= p.Radius || dist < 1e-4)
                        {
                            continue;
                        }

                        double atten = 1.0 - dist / p.Radius;
                        double ndl = System.Math.Max(0.0, -(nx * vx + ny * vy + nz * vz) / dist); // toward the light
                        double c = atten * atten * ndl;
                        lr += p.Colour.R * c;
                        lg += p.Colour.G * c;
                        lb += p.Colour.B * c;
                    }
                }

                int vi3 = i / 3;
                litR[vi3] = (float)System.Math.Clamp(lr, 0.03, 1.0);
                litG[vi3] = (float)System.Math.Clamp(lg, 0.03, 1.0);
                litB[vi3] = (float)System.Math.Clamp(lb, 0.03, 1.0);
            }

            for (int f = 0; f < cls.TriangleTextureIds.Length; f++)
            {
                int tex = cls.TriangleTextureIds[f];
                if (!byTex.TryGetValue(tex, out var g))
                {
                    byTex[tex] = g = ([], [], [], [], new Dictionary<(double, double, double, double, double, double, double, double), int>());
                }

                for (int k = 0; k < 3; k++)
                {
                    int vi = meshData.Indices[f * 3 + k];
                    double px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
                    float s = meshData.Uvs[vi * 2], t = meshData.Uvs[vi * 2 + 1];
                    double rs = R4(s), rt = R4(t);
                    double cr = R2(litR[vi]), cg = R2(litG[vi]), cb = R2(litB[vi]);
                    var key = (px, py, pz, rs, rt, cr, cg, cb);
                    if (!g.Weld.TryGetValue(key, out int idx))
                    {
                        idx = g.P.Count / 3;
                        g.Weld[key] = idx;
                        g.P.Add(px);
                        g.P.Add(py);
                        g.P.Add(pz);
                        g.U.Add((float)rs);
                        g.U.Add((float)rt);
                        g.C.Add((float)cr);
                        g.C.Add((float)cg);
                        g.C.Add((float)cb);
                        g.C.Add(1f);
                    }

                    g.I.Add(idx);
                }
            }
        }

        foreach (var (tex, g) in byTex.OrderBy(kv => kv.Key))
        {
            meshes.Add(new RuntimeMesh("moby", tex, g.P.ToArray(), g.U.ToArray(), g.I.ToArray(), g.C.ToArray()));
        }
    }

    /// <summary>Native Z-up direction/vector -> OBP Y-up (swap y/z).</summary>
    private static (double X, double Y, double Z) YUp((float X, float Y, float Z) v) => (v.X, v.Z, v.Y);

    private static double Dist2((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary>Class-local normal -> unit world normal in OBP Y-up space (rotate by the instance matrix 3x3, swap y/z, normalise).</summary>
    private static (double X, double Y, double Z) WorldNormal(double[] m, double nx, double ny, double nz)
    {
        double wx = m[0] * nx + m[4] * ny + m[8] * nz;
        double wy = m[1] * nx + m[5] * ny + m[9] * nz;
        double wz = m[2] * nx + m[6] * ny + m[10] * nz;
        double len = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);
        return len < 1e-9 ? (0, 1, 0) : (wx / len, wz / len, wy / len);
    }

    private static readonly int[][] MarkerCube =
    [
        [-1, -1, -1], [1, -1, -1], [1, 1, -1], [-1, 1, -1], [-1, -1, 1], [1, -1, 1], [1, 1, 1], [-1, 1, 1],
    ];

    private static readonly int[][] MarkerFaces =
    [
        [0, 1, 2], [0, 2, 3], [4, 6, 5], [4, 7, 6], [0, 4, 5], [0, 5, 1],
        [1, 5, 6], [1, 6, 2], [2, 6, 7], [2, 7, 3], [3, 7, 4], [3, 4, 0],
    ];

    private static void AddMobyMarkers(
        IReadOnlyList<GcInstances.MobyInstance> instances,
        Dictionary<int, GcMoby.MobyClass> classes,
        List<RuntimeMesh> meshes,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        if (instances.Count == 0 || double.IsPositiveInfinity(minX))
        {
            return;
        }

        double pad = 1.5 * System.Math.Max(maxX - minX, System.Math.Max(maxY - minY, maxZ - minZ));
        bool InRange(double x, double y, double z) =>
            x > minX - pad && x < maxX + pad && z > minY - pad && z < maxY + pad && y > minZ - pad && y < maxZ + pad;

        var positions = new List<double>();
        var indices = new List<int>();
        foreach (var moby in instances)
        {
            if (!InRange(moby.Position.X, moby.Position.Y, moby.Position.Z))
            {
                continue;
            }

            if (classes.TryGetValue(moby.OClass, out var cls) && cls.Mesh.Indices.Length > 0)
            {
                continue;
            }

            double sc = System.Math.Max(0.4, System.Math.Min(4, moby.Scale)) * 0.9;
            var m = GcInstances.MatrixFromPosRotScale(
                (moby.Position.X, moby.Position.Y, moby.Position.Z),
                (moby.Rotation.X, moby.Rotation.Y, moby.Rotation.Z), sc);
            int baseV = positions.Count / 3;
            foreach (var c in MarkerCube)
            {
                var (x, y, z) = GcInstances.TransformPoint(m, c[0], c[1], c[2]);
                positions.Add(R2(x));
                positions.Add(R2(z));
                positions.Add(R2(y));
            }

            foreach (var face in MarkerFaces)
            {
                indices.Add(baseV + face[0]);
                indices.Add(baseV + face[1]);
                indices.Add(baseV + face[2]);
            }
        }

        if (indices.Count > 0)
        {
            meshes.Add(new RuntimeMesh("moby-marker", -1, positions.ToArray(), [], indices.ToArray()));
        }
    }

    private static void AddSkyShell(GcSky.Shell shell, double scale, List<RuntimeMesh> meshes)
    {
        var byTex = new Dictionary<int, (List<double> P, List<float> U, List<float> C, List<int> I, Dictionary<(double, double, double, double, double, double), int> Weld)>();
        var order = new List<int>();

        for (int f = 0; f < shell.TriangleTextureIds.Length; f++)
        {
            int tex = shell.TriangleTextureIds[f];
            if (!byTex.TryGetValue(tex, out var g))
            {
                byTex[tex] = g = ([], [], [], [], new Dictionary<(double, double, double, double, double, double), int>());
                order.Add(tex);
            }

            for (int k = 0; k < 3; k++)
            {
                int vi = shell.Indices[f * 3 + k];
                double lx = shell.Positions[vi * 3] * scale;
                double ly = shell.Positions[vi * 3 + 1] * scale;
                double lz = shell.Positions[vi * 3 + 2] * scale;
                // native Z-up -> OBP Y-up, centred on origin (see AddSkyShell caller).
                double px = R2(lx), py = R2(lz), pz = R2(ly);
                double s = R4(shell.Uvs[vi * 2]), t = R4(shell.Uvs[vi * 2 + 1]);
                double a = R2(shell.Alpha[vi]);
                var key = (px, py, pz, s, t, a);
                if (!g.Weld.TryGetValue(key, out int idx))
                {
                    idx = g.P.Count / 3;
                    g.Weld[key] = idx;
                    g.P.Add(px);
                    g.P.Add(py);
                    g.P.Add(pz);
                    g.U.Add((float)s);
                    g.U.Add((float)t);
                    float av = (float)System.Math.Clamp(a, 0.0, 1.0);
                    g.C.Add(1f);
                    g.C.Add(1f);
                    g.C.Add(1f);
                    g.C.Add(av);
                }

                g.I.Add(idx);
            }
        }

        foreach (int tex in order)
        {
            var g = byTex[tex];
            meshes.Add(new RuntimeMesh("sky", tex, g.P.ToArray(), g.U.ToArray(), g.I.ToArray(), g.C.ToArray()));
        }
    }
}
