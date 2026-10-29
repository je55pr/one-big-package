using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.Godot.Player;
using OBP.RAC1.Player;
using OBP.RAC2.Gameplay;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Audio;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>World-scoped foreign weapon nodes and voices; source assets are borrowed.</summary>
internal sealed class FusionWeaponPresentationHost
{
    private readonly Dictionary<SourceWeaponKey, FusionMeshView> _heldViews = [];
    private readonly Dictionary<WeaponShotKey, FusionMeshView> _projectiles = [];
    private readonly List<(Node3D Node, int Remaining)> _effects = [];
    private readonly record struct AudioOwner(SourceWeaponKey Weapon, int? LoopSound);
    private readonly List<(int Due, AudioOwner Owner, FusionWeaponData.Voice Voice)> _pendingAudio = [];
    private readonly List<(AudioOwner Owner, AudioStreamPlayer Player)> _audioPlayers = [];
    private FusionMeshView? _beam;
    private Rac3ShockCannonBeam.Scroll _beamScroll;

    internal IReadOnlyDictionary<SourceWeaponKey, FusionMeshView> HeldViews => _heldViews;
    internal IReadOnlyDictionary<WeaponShotKey, FusionMeshView> Projectiles => _projectiles;
    internal int EffectCount => _effects.Count;
    internal int PendingAudioCount => _pendingAudio.Count;
    internal int AudioPlayerCount => _audioPlayers.Count;
    internal int AudioStarted { get; private set; }
    internal bool BeamVisible => _beam?.Visible == true;

    internal void ResetSelection()
    {
        StopAudio();
        HideBeam();
    }

    internal void Attach(Node3D avatar, Node3D world, FusionWeaponSession? session, FusionWeaponAssets? assets)
    {
        if (session is null || assets is null || _beam is not null || _heldViews.Count != 0) return;
        foreach (var key in new[] { FusionWeaponSession.Lancer, FusionWeaponSession.ShockCannon })
        {
            if (!assets.Available(key)) continue;
            var view = new FusionMeshView { Name = SourceIdentityWire.FormatSource(key.SourceGame) + "_original_weapon", Visible = false };
            avatar.AddChild(view);
            _heldViews.Add(key, view);
        }
        _beam = new() { Name = "rac3_original_beam", Visible = false };
        world.AddChild(_beam);
    }

    internal void SynchronizeProjectiles(WeaponFrame frame, FusionWeaponSession session, FusionWeaponAssets assets, Node3D world)
    {
        var active = session.LancerSession.ActiveShots.Select(shot => new WeaponShotKey(FusionWeaponSession.Lancer, shot.Generation, shot.Sequence)).ToHashSet();
        foreach (var (key, node) in _projectiles.ToArray())
            if (!active.Contains(key)) { node.QueueFree(); _projectiles.Remove(key); }
        foreach (var query in frame.Queries.Where(query => query.Shot.Weapon == FusionWeaponSession.Lancer && active.Contains(query.Shot)))
        {
            if (!_projectiles.TryGetValue(query.Shot, out var node))
            {
                node = new() { Name = "rac2_original_lancer_projectile" };
                world.AddChild(node);
                node.Sample(assets.Projectile, assets);
                _projectiles.Add(query.Shot, node);
            }
            node.GlobalPosition = ToGodot(query.To);
        }
    }

    internal void SpawnSpread(WeaponFrame frame, IReadOnlyList<WeaponContact> contacts, Node3D world)
    {
        // Bounded OBP feedback for the not-yet-decoded native spread renderer.
        // No substituted R&C1 effect texture/model is used.
        using var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var query in frame.Queries.Where(query => query.Kind == WeaponQueryKind.Ray))
        {
            float fraction = contacts.Where(contact => contact.QueryIndex == query.Index).Select(contact => contact.Fraction).DefaultIfEmpty(1).Min();
            mesh.SurfaceAddVertex(ToGodot(query.From));
            mesh.SurfaceAddVertex(ToGodot(NVector3.Lerp(query.From, query.To, fraction)));
        }
        mesh.SurfaceEnd();
        using var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.35f, .75f, 1) };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        world.AddChild(node);
        _effects.Add((node, 5));
    }

    internal void TickEffects()
    {
        for (int index = _effects.Count - 1; index >= 0; index--)
        {
            var item = _effects[index];
            if (--item.Remaining <= 0) { item.Node.QueueFree(); _effects.RemoveAt(index); }
            else _effects[index] = item;
        }
    }

    internal FusionWeaponModelSnapshot StepViews(Rac1PlayerAnimationPresentationController controller, FusionWeaponSession? session,
        FusionWeaponAssets? assets, PlayerAvatar? avatar, bool playerAlive, bool wrenchActive, bool waterHidden,
        Func<Vector3> muzzle, Vector3 forward, Func<Vector3, NVector3> nativeVector, FusionWeaponModelSnapshot model)
    {
        if (session is null || assets is null || avatar is null) return model;
        model = model with { Age = model.Age + 1 };
        foreach (var (key, view) in _heldViews)
        {
            view.Visible = key == session.Selected && session.Hand.IsHolding(key) && assets.Available(key) && playerAlive &&
                !wrenchActive && !waterHidden;
            if (!view.Visible) continue;
            // Native slot0 (semantic0/joint56), sampled after the source arm layers.
            var hand = controller.CurrentAttachmentPose(PlayerEquipmentPose.HandAttachmentKey);
            if (hand is not null)
            {
                Vector3 Point(double x, double y, double z) => PlayerAvatarView.ToGodotLocal(hand.Transform(new(x, y, z)), avatar.Axes, avatar.BaseHeight, true);
                Vector3 origin = Point(0, 0, 0);
                // Reflect X / swap YZ back through the avatar frame exactly once.
                var basis = new Basis(Point(-1, 0, 0) - origin, Point(0, 0, 1) - origin, Point(0, 1, 0) - origin);
                view.Transform = new(basis.Orthonormalized(), origin);
            }
            if (key == FusionWeaponSession.Lancer && assets.Lancer is { } lancer)
            {
                var clip = lancer.Model.Sequences.Single(sequence => sequence.Index == model.Sequence);
                int frame = model.Age / 2;
                if (frame >= clip.Frames.Count) { model = model with { Sequence = 1 }; frame = 0; }
                view.Sample(Rac2LancerPresentation.HeldModel(lancer, model.Sequence, frame), assets);
            }
            else if (assets.Cannon is { } cannon)
            {
                // Bounded OBP clip selection; native player-to-held selection
                // remains unrecovered.
                int sequence = session.CannonFrame?.ChargeUpdates > 0 ? 2 : 0;
                int frame = (model.Age / 2) % cannon.Sequences.Single(clip => clip.Index == sequence).Frames.Count;
                view.Sample(Rac3ShockCannonAssets.Model(cannon, sequence, frame), assets);
            }
        }
        if (_beam is null) return model;
        _beam.Visible = session.CannonFrame?.Effects.Any(effect => effect.Kind == Rac3ShockCannonEffectKind.Beam) == true;
        if (!_beam.Visible) return model;
        _beamScroll = _beamScroll.Advance();
        // Convert host Y-up back through the source Z-up beam projector.
        var basisForBeam = Rac3ShockCannonBeam.Basis.FromNativeAim(nativeVector(forward), nativeVector(muzzle()));
        _beam.Sample(Rac3ShockCannonBeam.Build(session.CannonFrame!, basisForBeam, _beamScroll).Select(layer => layer.Mesh).ToArray(), assets);
        return model;
    }

    internal void QueueAudio(WeaponAudioIntent cue, FusionWeaponSession session, FusionWeaponAssets assets)
    {
        if (cue.Operation == WeaponAudioOperation.StopLoop) { StopAudio(new AudioOwner(cue.Weapon, cue.LocalSound)); return; }
        var owner = new AudioOwner(cue.Weapon, cue.Operation == WeaponAudioOperation.StartLoop ? cue.LocalSound : null);
        if (owner.LoopSound.HasValue && (_pendingAudio.Any(pending => pending.Owner == owner) ||
            _audioPlayers.Any(playing => playing.Owner == owner))) return;
        foreach (var voice in assets.Audio(cue)) _pendingAudio.Add((session.Tick + (int)Math.Ceiling(voice.Delay * 60), owner, voice));
    }

    internal void TickAudio(int tick, Node parent)
    {
        foreach (var pending in _pendingAudio.Where(pending => pending.Due <= tick).ToArray())
        {
            _pendingAudio.Remove(pending);
            var voice = pending.Voice;
            if (!RuntimeAudioHost.TryPrepare(new(voice.Clip, RuntimeAudioCategory.SoundEffect, voice.Loop, voice.Gain), out var clip, out var problem))
            {
                GD.PrintErr("[fusion] skipping unavailable weapon voice: " + problem);
                continue;
            }
            using var stream = new AudioStreamWav
            {
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                Data = clip!.Pcm16LittleEndian,
                MixRate = clip.SampleRateHz,
                LoopMode = voice.Loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
                LoopBegin = clip.LoopStartFrame,
                LoopEnd = clip.LoopEndFrame ?? 0
            };
            var player = new AudioStreamPlayer { Stream = stream, VolumeDb = clip.VolumeDb };
            parent.AddChild(player);
            var owned = (pending.Owner, player);
            _audioPlayers.Add(owned);
            player.Finished += () => { _audioPlayers.Remove(owned); player.QueueFree(); };
            player.Play();
            AudioStarted++;
        }
    }

    private void StopAudio(AudioOwner? owner, bool freeImmediately = false)
    {
        _pendingAudio.RemoveAll(pending => owner is null || pending.Owner == owner);
        foreach (var owned in _audioPlayers.Where(playing => owner is null || playing.Owner == owner).ToArray())
        {
            if (GodotObject.IsInstanceValid(owned.Player))
            {
                owned.Player.Stop();
                if (freeImmediately) owned.Player.Free(); else owned.Player.QueueFree();
            }
            _audioPlayers.Remove(owned);
        }
    }

    internal void StopAudio(bool freeImmediately = false) => StopAudio(null, freeImmediately);

    internal void SetPaused(bool paused)
    {
        foreach (var owned in _audioPlayers)
            if (GodotObject.IsInstanceValid(owned.Player)) owned.Player.StreamPaused = paused;
    }

    internal void HideBeam()
    {
        if (_beam is not null && GodotObject.IsInstanceValid(_beam)) _beam.Visible = false;
    }

    internal void Clear(bool freeImmediately = false)
    {
        StopAudio(freeImmediately);
        foreach (var node in _projectiles.Values.Cast<Node3D>().Concat(_effects.Select(effect => effect.Node)))
            if (GodotObject.IsInstanceValid(node)) { if (freeImmediately) node.Free(); else node.QueueFree(); }
        _projectiles.Clear();
        _effects.Clear();
        HideBeam();
        _beamScroll = default;
    }

    internal void ReleaseViews(bool freeImmediately = false)
    {
        foreach (var view in _heldViews.Values)
            if (GodotObject.IsInstanceValid(view)) { if (freeImmediately) view.Free(); else view.QueueFree(); }
        _heldViews.Clear();
        if (_beam is not null && GodotObject.IsInstanceValid(_beam)) { if (freeImmediately) _beam.Free(); else _beam.QueueFree(); }
        _beam = null;
    }

    private static Vector3 ToGodot(NVector3 value) => new(value.X, value.Y, value.Z);
}
