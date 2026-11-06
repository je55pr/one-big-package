using Godot;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Class 806 Nanotech clouds: the retail cluster mesh, orb sprites, sparkle trails, curved flights and the
/// healing glow on Ratchet. Native rules live in <see cref="Rac1NanotechCluster"/> (OBP.RAC1); admission and
/// the single health point stay in <see cref="Rac1NanotechCloudSession"/>. This partial owns only Godot nodes.
/// </summary>
internal sealed partial class Rac1LootPresentation
{
    private Rac1NanotechOrbAssets? _orbAssets;
    private bool _orbAssetsFailed;
    private Rac1NanotechOrbSpin _orbSpin = new();
    private Rac1ParticleSession? _orbParticles;
    private Rac1ParticleView? _orbParticleView;
    private Rac1FxMaterials? _orbMaterials;
    private ImmediateMesh? _orbMesh;
    private MeshInstance3D? _orbMeshNode;
    private readonly Dictionary<int, Rac1NanotechCluster> _rac1NanotechRiders = [];
    private readonly List<Rac1HostedNanotechCloud> _rac1NanotechClouds = [];
    private readonly List<Rac1NanotechCluster> _rac1HealingClusters = [];
    private const float OrbVisibleDistance = 48;
    private List<Rac1NanotechCluster> _orbDrawn = [];

    /// <summary>A broken crate's cloud awaiting Ratchet; its cluster is null only if the level's retail tables were unavailable.</summary>
    internal sealed class Rac1HostedNanotechCloud(Rac1NanotechCloudSession motion, Rac1NanotechCluster? cluster)
    {
        public Rac1NanotechCloudSession Motion { get; } = motion;
        public Rac1NanotechCluster? Cluster { get; } = cluster;
        /// <summary>Invisible 45-tick fallback flight, used only when the retail orb tables are unavailable.</summary>
        public int FallbackTicks { get; set; }
    }

    internal int NanotechRiderCount => _rac1NanotechRiders.Count;
    internal int NanotechHealingCount => _rac1HealingClusters.Count;
    internal Rac1ParticleSession? NanotechParticles => _orbParticles;

    private bool EnsureNanotechOrbs()
    {
        if (_orbAssets is not null) return true;
        if (_orbAssetsFailed || _sceneResult is null || _world is not { } world || _ports.SourcePath() is not { } path) return false;
        try
        {
            using var reader = new FileRandomAccessReader(path);
            var level = Rac1DiscIndex.Read(reader).Levels.Single(candidate => candidate.LevelId == world.LevelId);
            _orbAssets = Rac1NanotechOrbAssets.Decode(Rac1ExecutableOverlay.Read(reader, level), Rac1LevelCore.Open(reader, level));
        }
        catch (InvalidDataException exception)
        {
            _orbAssetsFailed = true;
            GD.PrintErr($"[rac1-crates] Nanotech orb retail tables unavailable for level {world.LevelId}; clouds are invisible: {exception.Message}");
            return false;
        }
        _orbParticles = new(_orbAssets.Particles, Rac1EffectRandom);
        _orbMaterials = new(_orbAssets.FxTextures, priorityBias: -10); // the original draws particles after its mesh callbacks
        _orbMesh = new();
        // World-space vertices; the node ignores its parent's transform.
        _orbMeshNode = new MeshInstance3D { Name = "Rac1NanotechOrbMesh", Mesh = _orbMesh, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _sceneResult.Root.AddChild(_orbMeshNode);
        _orbParticleView = new Rac1ParticleView { Name = "Rac1NanotechParticles" };
        _sceneResult.Root.AddChild(_orbParticleView);
        return true;
    }

    private static bool IsNanotechCrate(RuntimeWorldScene.DynamicObjectNode node) => node.Source.NativeClassId == Rac1ClassIds.NanotechCrate;

    internal void SpawnRac1NanotechCloud(RuntimeWorldScene.DynamicObjectNode crate, Vector3 origin)
    {
        var motion = new Rac1NanotechCloudSession(ToEffectNative(origin + Vector3.Up * .5f));
        Rac1NanotechCluster? cluster = null;
        if (EnsureNanotechOrbs())
        {
            // The intact crate already hosted this cluster (state 1); breaking it drops to state 2.
            if (!_rac1NanotechRiders.Remove(crate.Source.InstanceIndex, out cluster))
                cluster = new(ToEffectNative(origin), _orbAssets!, _orbSpin, _orbParticles!, Rac1EffectRandom);
            cluster.CrateBroken();
        }
        _rac1NanotechClouds.Add(new(motion, cluster));
    }

    private void TickRac1NanotechOrbs(NVector3 root, NVector3 socket)
    {
        if (_player is null || _sceneResult is null) return;
        bool crates = _presentation.CrateNodes.Any(IsNanotechCrate) || _rac1NanotechClouds.Count > 0 || _rac1HealingClusters.Count > 0;
        if (!crates) return;
        if (!EnsureNanotechOrbs()) { TickFallbackNanotechClouds(root, socket); return; }
        var assets = _orbAssets!; var k = assets.K;
        var camera = _sceneResult.Root.GetViewport()?.GetCamera3D();
        Vector3 eyeGodot = camera?.GlobalPosition ?? _player.GlobalPosition + Vector3.Up * 5;
        var eye = ToEffectNative(eyeGodot);
        bool Visible(NVector3 native)
        {
            var point = FromEffectNative(native);
            return camera is null || (eyeGodot.DistanceTo(point) < OrbVisibleDistance && camera.IsPositionInFrustum(point));
        }
        _orbSpin.Step(k.SpinStep, k.OrbRadius);
        var drawn = new List<Rac1NanotechCluster>();

        foreach (var node in _presentation.CrateNodes.Where(IsNanotechCrate))
        {
            int id = node.Source.InstanceIndex;
            bool intact = IsInstanceValid(node.Root) && node.Root.Visible;
            if (!intact)
            {
                if (_rac1NanotechRiders.Remove(id, out var gone)) gone.Dispose();
                continue;
            }
            var position = ToEffectNative(node.Root.GlobalPosition);
            if (!_rac1NanotechRiders.TryGetValue(id, out var rider))
                _rac1NanotechRiders.Add(id, rider = new(position, assets, _orbSpin, _orbParticles!, Rac1EffectRandom));
            rider.Follow(position);
            bool visible = Visible(position);
            rider.Step(new(root, socket, NVector3.UnitZ, visible));
            if (visible) drawn.Add(rider);
        }

        foreach (var cloud in _rac1NanotechClouds.ToArray())
        {
            var cluster = cloud.Cluster!;
            int pending = _rac1NanotechClouds.Count(other => other != cloud && other.Motion.Attracting && !other.Motion.Collected);
            if (!cloud.Motion.Attracting && cloud.Motion.TryAttract(root, _rac1Nanotech.Probe(), pending))
                cluster.Launch(socket, NVector3.UnitZ);
            bool visible = Visible(cluster.Position);
            var result = cluster.Step(new(root, socket, NVector3.UnitZ, visible));
            if (visible) drawn.Add(cluster);
            if (!result.FirstArrival) continue;
            // The first orb to arrive credits the one health point and plays class501 sound1; later orbs credit nothing.
            cloud.Motion.Arrive(_rac1Nanotech);
            PlayRac1PickupSound(Rac1ClassIds.NanotechCrate, Rac1CrateAudio.NanotechArrivalLocalSound, FromEffectNative(socket));
            _rac1NanotechClouds.Remove(cloud);
            _rac1HealingClusters.Add(cluster);
            RefreshRac1HudState();
        }
        foreach (var cluster in _rac1HealingClusters.ToArray())
            if (cluster.Step(new(root, socket, NVector3.UnitZ, true)).Finished)
            {
                cluster.Dispose();
                _rac1HealingClusters.Remove(cluster);
            }

        _orbParticles!.Hero = root;
        _orbParticles.Step(eye, enforceLevelBounds: false);
        _orbParticleView!.Render(_orbParticles, assets.Particles.PartTextures);
        _orbDrawn = drawn;
    }

    /// <summary>Once per rendered frame: the billboard mesh faces the current camera (the native draw callback runs every frame).</summary>
    internal void PresentRac1NanotechOrbs()
    {
        if (_orbAssets is null || _orbMaterials is null || _orbMesh is null || _player is null || _sceneResult is null) return;
        var camera = _sceneResult.Root.GetViewport()?.GetCamera3D();
        var eye = ToEffectNative(camera?.GlobalPosition ?? _player.GlobalPosition + Vector3.Up * 5);
        _orbMaterials.Write(_orbMesh, _orbDrawn.Where(cluster => cluster.State is Rac1NanotechCluster.Phase.Riding or Rac1NanotechCluster.Phase.Resting)
            .SelectMany(cluster => cluster.Build(eye)), FromEffectNative);
    }
    private void TickFallbackNanotechClouds(NVector3 root, NVector3 socket)
    {
        foreach (var cloud in _rac1NanotechClouds.ToArray())
        {
            int pending = _rac1NanotechClouds.Count(other => other != cloud && other.Motion.Attracting && !other.Motion.Collected);
            cloud.Motion.TryAttract(root, _rac1Nanotech.Probe(), pending);
            if (!cloud.Motion.Attracting || ++cloud.FallbackTicks < 45) continue;
            cloud.Motion.Arrive(_rac1Nanotech);
            PlayRac1PickupSound(Rac1ClassIds.NanotechCrate, Rac1CrateAudio.NanotechArrivalLocalSound, FromEffectNative(socket));
            _rac1NanotechClouds.Remove(cloud);
            RefreshRac1HudState();
        }
    }

    internal void ResetRac1NanotechOrbs()
    {
        foreach (var cluster in _rac1NanotechRiders.Values.Concat(_rac1NanotechClouds.Select(cloud => cloud.Cluster)
            .OfType<Rac1NanotechCluster>()).Concat(_rac1HealingClusters))
            cluster.Dispose();
        _rac1NanotechRiders.Clear(); _rac1NanotechClouds.Clear(); _rac1HealingClusters.Clear();
        _orbParticles?.Clear(); _orbParticles = null;
        foreach (Node? node in new Node?[] { _orbParticleView, _orbMeshNode })
            if (node is not null && IsInstanceValid(node) && !node.IsQueuedForDeletion()) node.QueueFree();
        _orbParticleView = null; _orbMeshNode = null; _orbMesh = null; _orbDrawn = [];
        _orbMaterials?.Dispose(); _orbMaterials = null;
        _orbAssets = null; _orbAssetsFailed = false; _orbSpin = new();
    }
}
