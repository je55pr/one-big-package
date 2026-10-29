using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed partial class Rac1LootPresentation
{
    private readonly List<(Node3D Node, Rac1AmmoPickupSession Motion)> _rac1AmmoPickups = [];
    private readonly List<Rac1HostedNanotechCloud> _rac1NanotechClouds = [];

    internal void ResetRac1CrateSupply()
    {
        foreach (var node in _rac1AmmoPickups.Select(pickup => pickup.Node).Concat(_rac1NanotechClouds.Select(cloud => cloud.Node)))
            if (IsInstanceValid(node)) node.QueueFree();
        _rac1AmmoPickups.Clear();
        _rac1NanotechClouds.Clear();
    }

    internal void SpawnRac1CrateSupply(RuntimeWorldScene.DynamicObjectNode crate, Vector3 origin)
    {
        if (_sceneResult is null) return;
        if (crate.Source.NativeClassId == Rac1CrateSupply.NanotechCrateClass)
        {
            var node = new Node3D { Name = "Rac1NanotechCloud_HostParticlePresentation" };
            // Class806's one-point/eight-particle rules are recovered. These
            // visible wisps are host presentation until its sprite program is decoded.
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(.2f, .75f, 1),
                EmissionEnabled = true,
                Emission = new Color(.2f, .6f, 1),
            };
            for (int i = 0; i < Rac1NanotechCloudSession.ParticleCount; i++)
                node.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .035f, Height = .07f }, MaterialOverride = material });
            _sceneResult.Root.AddChild(node);
            node.GlobalPosition = origin + Vector3.Up * .5f;
            _rac1NanotechClouds.Add(new(node, new(ToEffectNative(node.GlobalPosition))));
        }
        else if (crate.Source.NativeClassId == 511)
        {
            var definition = Rac1CrateSupply.SelectFirstAmmo(crate.Source, _rac1Weapons, Rac1EffectRandom);
            if (_rac1PickupAssets?.Models.ContainsKey(definition.ModelClass) != true)
            {
                GD.PrintErr($"[rac1-crates] ammo model {definition.ModelClass} missing for preferred item {definition.Item}");
                return;
            }
            int count = Rac1CrateSupply.SelectFirstAmmoPieceCount(Rac1EffectRandom);
            for (int i = 0; i < count; i++)
            {
                var motion = new Rac1AmmoPickupSession(definition, ToEffectNative(origin), Rac1EffectRandom);
                var node = CreateRac1EffectNode(definition.ModelClass, $"Rac1RetailAmmo_item{definition.Item}");
                node.GlobalPosition = origin;
                _rac1AmmoPickups.Add((node, motion));
            }
        }
    }

    internal void TickRac1CrateSupply(NVector3 root, NVector3 socket)
    {
        for (int i = _rac1AmmoPickups.Count - 1; i >= 0; i--)
        {
            var pickup = _rac1AmmoPickups[i];
            var contact = ProbeRac1EffectContact(pickup.Motion.Position,
                pickup.Motion.Position + pickup.Motion.Velocity, .4f);
            pickup.Motion.Step(root, _rac1Weapons, !_rac1Nanotech.Probe().IsDead, contact);
            pickup.Node.GlobalPosition = FromEffectNative(pickup.Motion.Position);
            pickup.Node.Rotation = new(0, pickup.Motion.Rotation, 0);
            if (!pickup.Motion.Collected) continue;
            pickup.Node.QueueFree();
            _rac1AmmoPickups.RemoveAt(i);
            PersistRac1CampaignState("crate ammo collection");
            RefreshRac1HudState();
        }
        foreach (var cloud in _rac1NanotechClouds.ToArray())
        {
            int pending = _rac1NanotechClouds.Count(other => other != cloud && other.Motion.Attracting && !other.Motion.Collected);
            cloud.Motion.TryAttract(root, _rac1Nanotech.Probe(), pending);
            cloud.Age += 1f / 60;
            if (cloud.Motion.Attracting) cloud.Travel = Math.Min(1, cloud.Travel + 1f / 45);
            var position = FromEffectNative(NVector3.Lerp(cloud.Motion.Origin, socket, cloud.Travel));
            cloud.Node.GlobalPosition = position;
            int index = 0;
            foreach (var particle in cloud.Node.GetChildren().OfType<MeshInstance3D>())
            {
                float phase = cloud.Age * 4 + index++ * MathF.Tau / Rac1NanotechCloudSession.ParticleCount;
                particle.Position = new Vector3(MathF.Cos(phase), MathF.Sin(phase * .7f), MathF.Sin(phase)) * .22f * (1 - cloud.Travel);
            }
            if (cloud.Travel < 1) continue;
            cloud.Motion.Arrive(_rac1Nanotech);
            PlayRac1PickupSound(Rac1CrateSupply.NanotechCrateClass, Rac1CrateAudio.NanotechArrivalLocalSound, position);
            cloud.Node.QueueFree();
            _rac1NanotechClouds.Remove(cloud);
            RefreshRac1HudState();
        }
    }

    internal sealed class Rac1HostedNanotechCloud(Node3D node, Rac1NanotechCloudSession motion)
    {
        public Node3D Node { get; } = node;
        public Rac1NanotechCloudSession Motion { get; } = motion;
        public float Age { get; set; }
        public float Travel { get; set; }
    }
}
