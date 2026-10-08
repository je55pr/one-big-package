using System.Buffers.Binary;
using OBP.Core.Math;
using OBP.IO;
using OBP.PS2.Collision;
using OBP.PS2.Compression;
using OBP.PS2.Iso;
using OBP.PS2.Geometry;
using OBP.PS2.Graphics;
using OBP.PS2.Presentation;
using OBP.RAC2.Audio;
using OBP.RAC2.Geometry;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Presentation;

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
public static partial class GcWorldImport
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
        var authoredPlayerStart = BuildAuthoredPlayerStart(gameplay);
        var mobyClasses = GcMobyClasses.Read(Core());

        CollectTextures("tie", GcLevelTextures.Table.Tie);
        var tieClasses = GcTie.ReadClasses(Core());
        PlaceMatrixInstances("tie", gameplay.TieInstances,
            id => tieClasses.TryGetValue(id, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds,
                    PresentationsFor(
                        c.Mesh.Materials, c.Mesh.TriangleMaterialStateIndices, null,
                        c.TriangleTextureIds.Length))
                : null,
            meshes, Grow, growBounds: true);

        CollectTextures("shrub", GcLevelTextures.Table.Shrub);
        var shrubClasses = GcShrub.ReadClasses(Core());
        PlaceMatrixInstances("shrub", gameplay.ShrubInstances,
            id => shrubClasses.TryGetValue(id, out var c)
                ? (c.Mesh.Positions, c.Mesh.Uvs, c.Mesh.Indices, c.TriangleTextureIds,
                    PresentationsFor(
                        c.Mesh.Materials, c.Mesh.TriangleMaterialStateIndices,
                        c.Mesh.TriangleAlphaBlendEnabled, c.TriangleTextureIds.Length))
                : null,
            meshes, Grow, growBounds: true);

        CollectTextures("moby", GcLevelTextures.Table.Moby);
        var animatedMeshes = BuildAnimatedMeshes(gameplay, mobyClasses, out var animatedInstances);
        var dynamicClasses = new HashSet<int> { 500 };
        if (level == 0)
        {
            // Aranos opening lift, first progression door and MSR I family need
            // per-instance runtime identity rather than remaining welded into the
            // static Moby soup.
            dynamicClasses.Add(GcAranosOpeningLiftSession.NativeClassId);
            dynamicClasses.Add(GcAranosOpeningDoorSession.NativeClassId);
            dynamicClasses.Add(GcClass2827HostileSession.NativeClassId);
        }
        var dynamicObjects = BuildDynamicMobyObjects(
            level,
            gameplay,
            mobyClasses,
            gameplay.DirLights,
            gameplay.PointLights,
            dynamicClasses,
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
        short? levelMusicTrack = null;
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
            var fogSource = RuntimeAtmosphereSource.NativeLevelSettings;
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
                levelMusicTrack = n.MusicTrack;
            }

            if (nearestWithFog?.Fog is { } ef)
            {
                fogColour = (ef.Colour.R, ef.Colour.G, ef.Colour.B);
                fogNear = ef.NearDistance;
                fogFar = ef.FarDistance;
                fogNearI = ef.NearIntensity;
                fogFarI = ef.FarIntensity;
                fogSource = RuntimeAtmosphereSource.NativeEnvironmentSample;
            }

            environment = new RuntimeEnvironment(
                DeathHeight: s.DeathHeight,
                IsSphericalWorld: s.IsSphericalWorld,
                BackgroundColour: s.BackgroundColour,
                FogColour: fogColour,
                FogNearDistance: fogNear,
                FogFarDistance: fogFar,
                FogNearIntensity: fogNearI,
                FogFarIntensity: fogFarI,
                AmbientColour: ambient,
                BackgroundSource: RuntimeAtmosphereSource.NativeLevelSettings,
                FogSource: fogSource,
                AmbientSource: ambient is not null
                    ? RuntimeAtmosphereSource.NativeEnvironmentSample
                    : RuntimeAtmosphereSource.PresentationFallback);

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

        // --- representative runtime audio ---
        // Music uses the environment sample nearest the authored ship position.
        // The extra one-shot is deliberately only an integration cue: the GC
        // SBlk event/remap path still lacks a proven gameplay-event mapping and
        // playback-rate conversion, so do not label this as a crate/pickup sound.
        IReadOnlyList<RuntimeAudioPlaybackIntent>? levelAudio = null;
        RuntimeAudioClip? representativeOneShot = null;
        GcLevelAudioWad? nativeAudio = null;
        try
        {
            var audioReader = fs.OpenFile($"/G/AUDIO{level}.WAD");
            if (audioReader is not null)
            {
                nativeAudio = GcLevelAudioWad.Open(audioReader);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
        {
            nativeAudio = null;
        }

        if (nativeAudio is not null)
        {
            if (levelMusicTrack is { } musicTrack)
            {
                try
                {
                    levelAudio = [GcRuntimeAudio.DecodeLevelMusic(nativeAudio, musicTrack)];
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
                {
                    levelAudio = null;
                }
            }

            if (nativeAudio.UpgradeSample.Present)
            {
                try
                {
                    representativeOneShot = GcRuntimeAudio.DecodeRepresentativeOneShot(nativeAudio);
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or OverflowException)
                {
                    representativeOneShot = null;
                }
            }
        }

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
            DynamicObjects: dynamicObjects,
            PlayerStart: authoredPlayerStart,
            LevelAudio: levelAudio,
            RepresentativeAudioOneShot: representativeOneShot);
    }

    /// <summary>
    /// Resolve the unique authored class-0 placement as Ratchet's level entry.
    /// The GC authority set carries exactly one class-0 instance at index 0 in
    /// every observed level. This remains distinct from the level-settings ship
    /// tuple, which is the repeated (20,20,20,0) default on Aranos and several
    /// non-planet/special destinations.
    /// </summary>
}
