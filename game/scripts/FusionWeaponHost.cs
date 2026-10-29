using Godot;
using OBP.Core;
using OBP.IO;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC3.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using OBP.Runtime.Presentation;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

/// <summary>
/// Application equipment composition. Native sessions own firing rules and persistent
/// ammunition; attached sources own prepared assets; a scene lease owns every renderer.
/// </summary>
internal sealed class FusionWeaponHost(Node owner, FusionWeaponPlayerPorts player,
    FusionWeaponSourcePorts source, FusionWeaponWorldActions actions, Func<Node3D?> avatarView,
    FusionWeaponNativePresentationPorts nativePresentation) : IDisposable
{
    private readonly FusionWeaponPreparationNotice _notice = new(owner);
    private WorldBinding? _world;
    private Rac1FusionCampaignIsolation? _fixture;
    private bool _persistentSession, _deathCleared, _disposed;
    private WeaponStatus? _displayedStatus;
    private SourceWeaponKey? _displayedHand;
    private FusionWeaponModelSnapshot _modelPose;
    private static readonly IReadOnlyDictionary<SourceWeaponKey, FusionMeshView> NoHeldViews = new Dictionary<SourceWeaponKey, FusionMeshView>();
    private static readonly IReadOnlyDictionary<WeaponShotKey, FusionMeshView> NoProjectiles = new Dictionary<WeaponShotKey, FusionMeshView>();

    internal FusionWeaponSourceHost<FusionWeaponAssets> Sources { get; } = new(data => new(data),
        attached => new(attached, new FileVerificationCache(ProjectSettings.GlobalizePath("user://source-verification"))));
    internal FusionWeaponSession? Session { get; private set; }
    internal bool TestActive => _fixture is not null;
    internal Rac1WeaponInventory? OriginalInventory => _fixture?.OriginalInventory;
    internal bool CanRunFixture => !player.Options().Ordinary && player.Options().DestinationId == "rac1:LEVEL0";
    internal PlaytestWeaponConfiguration? Configuration => Session is null ? null : Sources.Assets?.Configuration(TestActive);
    internal bool MovementStateBlocked => TestActive || Session?.Selected.SourceGame is ObpSourceGame.Rac2 or ObpSourceGame.Rac3 || Session?.LancerSession.ActiveShots.Count > 0;
    internal int AcceptedHits { get; private set; }
    internal int AudioStarted { get; private set; }
    internal WeaponDamageIntent? LastDamage { get; private set; }
    internal FusionWeaponPoseSnapshot Pose { get; private set; }
    internal IReadOnlyDictionary<SourceWeaponKey, FusionMeshView> HeldViews => _world?.Presentation.HeldViews ?? NoHeldViews;
    internal IReadOnlyDictionary<WeaponShotKey, FusionMeshView> Projectiles => _world?.Presentation.Projectiles ?? NoProjectiles;
    internal int EffectCount => _world?.Presentation.EffectCount ?? 0;
    internal int PendingAudioCount => _world?.Presentation.PendingAudioCount ?? 0;
    internal int AudioPlayerCount => _world?.Presentation.AudioPlayerCount ?? 0;
    internal bool BeamVisible => _world?.Presentation.BeamVisible == true;

    internal void BindPersistent(FusionWeaponSession session)
    {
        Session = session;
        _persistentSession = true;
    }

    internal Node3D CreateNativeEffect(int nativeClass, string name) => nativePresentation.CreateEffect(nativeClass, name);
    internal void PlayNativeSound(int nativeClass, int localSound, Vector3 position) => nativePresentation.PlaySound(nativeClass, localSound, position);
    internal void ConfigureNativePresentation(RuntimeWorld world) => nativePresentation.Configure(world);

    internal IDisposable BindWorld(Node3D root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _world?.Dispose();
        return _world = new(this, root);
    }

    internal void Configure(RuntimeWorld world)
    {
        var options = player.Options();
        if (options.Persistent is { } persistent) { BindPersistent(persistent); return; }
        if (options.LegacyReplayWithoutWeapons) return;
        AcceptedHits = AudioStarted = 0;
        LastDamage = null;
        _deathCleared = false;
        Pose = default;
        _displayedStatus = null;
        _persistentSession = false;
        _modelPose = default;
        if (!options.IsolatedTest || world.LevelId != 0)
        {
            Session = new(options.Seed, player.Inventory(), player.Hand());
            return;
        }
        _fixture = player.IsolateCampaign(world.PlayerStart!);
        Session = new(options.Seed, hand: player.Hand());
        GD.Print("[fusion] opt-in Veldin test: Bomb Glove [rac1/10] 10; Lancer [rac2/30] 40; Shock Cannon [rac3/43] 20. Original damage; campaign isolated.");
    }

    internal void PrepareSources(bool force = false)
    {
        source.RefreshPlayerSources();
        var attached = source.Attached().ToArray();
        if (!force && Sources.Matches(attached)) return;
        ResetSources();
        Sources.Prepare(attached, force: true);
        ShowPreparation(Sources.Preparation?.Status);
        GD.Print("[fusion] preparing shared trilogy weapon data in the background");
    }

    internal void TickPreparation()
    {
        if (!Sources.Preparing) return;
        string? previous = Sources.LoadingStatus;
        bool completed = Sources.Tick(_ =>
        {
            try
            {
                if (avatarView() is { } avatar) AttachViews(avatar);
                source.BindPresentation();
            }
            catch
            {
                // A partial attachment must not outlive the assets being retired.
                _world?.Presentation.ReleaseViews(freeImmediately: true);
                throw;
            }
        }, GD.Print, GD.PrintErr);
        if (!completed && previous == Sources.LoadingStatus) return;
        ShowPreparation(completed ? Sources.Failure : Sources.LoadingStatus);
        if (completed) player.RefreshAvailability();
        player.RefreshHud();
    }

    internal void ResetSources()
    {
        source.InvalidateActors();
        ClearTransients();
        _world?.Presentation.ReleaseViews();
        Sources.Reset();
        ShowPreparation(null);
    }

    private void ShowPreparation(string? status)
    {
        var options = player.Options();
        _notice.Show(status, options.Ordinary || options.HidePreparation);
    }

    internal static string WeaponName(SourceWeaponKey key) => key.SourceGame == ObpSourceGame.Rac1
        ? Rac1WeaponProfiles.Name((Rac1WeaponId)key.NativeItemId) : key == FusionWeaponSession.Lancer ? "Lancer" : "Shock Cannon";

    private string? Problem(SourceWeaponKey key) => Sources.Assets?.Problems.GetValueOrDefault(key) ??
        (key.SourceGame != ObpSourceGame.Rac1 ? Sources.Preparation?.Status ?? Sources.Failure : null);

    internal string Detail(SourceWeaponKey key) => Problem(key) ??
        (Session is { } session ? $"{session.Status(key).Ammo}/{session.Status(key).Capacity} · {session.Status(key).State}" : "");

    internal void SynchronizeNativeSelection()
    {
        if (Session is not { } session) return;
        bool foreign = session.Selected.SourceGame != ObpSourceGame.Rac1;
        session.BindRac1Inventory(player.Inventory());
        session.Select(new(ObpSourceGame.Rac1, player.Inventory().CurrentItemId));
        if (!foreign) return;
        StopAudio();
        Pose = default;
        _world?.Presentation.HideBeam();
    }

    internal void ToggleTest()
    {
        var options = player.Options();
        if (options.Recording) { player.Report("Choose the fusion loadout before starting an input recording."); return; }
        if (options.Ordinary) { player.Report("Launch with --fusion-test to use the isolated native Veldin fixture loadout."); return; }
        if (CanRunFixture) player.ReloadFixture(!TestActive);
    }

    internal void Select(SourceWeaponKey key)
    {
        if (player.Options().Persistent is not null)
        {
            var entry = player.Equipment().SingleOrDefault(entry => entry.Definition.Item == key);
            if (entry?.Selectable != true) { player.Report(entry?.Reason ?? "No implemented equipment registration."); return; }
        }
        if (Session is null || key.SourceGame == ObpSourceGame.Rac1 && !TestActive)
        {
            if (key.SourceGame == ObpSourceGame.Rac1) player.SelectNative((Rac1WeaponId)key.NativeItemId);
            player.RefreshAvailability();
            return;
        }
        var previous = Session.Selected;
        if (!Session.Select(key)) return;
        if (previous != key)
        {
            player.SelectionChanged(previous);
            _world?.Presentation.ResetSelection();
            _modelPose = default;
            Pose = default;
        }
        if (key.SourceGame == ObpSourceGame.Rac1) player.HoldNative();
        player.RefreshAvailability();
        player.RefreshHeldVisibility();
        player.RefreshHud();
        GD.Print($"[fusion] equipped {key}: {Detail(key)}");
    }

    internal HudPresentationState Hud(HudPresentationState state)
    {
        if (player.Options().Persistent is { } native)
        {
            var entry = player.Equipment().Single(entry => entry.Definition.Item == native.Selected);
            return state with
            {
                CurrentWeapon = new(entry.Definition.Item!.Value.ToString(), entry.Definition.DisplayName,
                    entry.State.Capacity is { } capacity ? new HudAmmo(entry.State.Ammo!.Value, capacity) : null)
                {
                    Status = entry.Reason ?? (native.Hand.WrenchHeld && entry.Definition.Item != PlayerEquipmentRegistrations.Wrench
                        ? "Wrench held · use to draw" : native.Status(entry.Definition.Item.Value).State)
                }
            };
        }
        if (Session is null || !TestActive && Session.Selected.SourceGame == ObpSourceGame.Rac1) return state;
        var status = Session.Status(Session.Selected);
        return state with
        {
            CurrentWeapon = new(status.Weapon.ToString(), $"{WeaponName(status.Weapon)} [{SourceIdentityWire.FormatSource(status.Weapon.SourceGame)}]", new(status.Ammo, status.Capacity))
            {
                Status = Problem(status.Weapon) ?? (Session.Hand.WrenchHeld && Session.Selected.SourceGame != ObpSourceGame.Rac1
                    ? "Wrench held · use to draw" : $"{(TestActive ? "Fusion test · " : "")}{status.State}")
            }
        };
    }

    internal void AttachViews(Node3D avatar)
    {
        if (_world is { } world) world.Presentation.Attach(avatar, world.Root, Session, Sources.Assets);
    }

    internal void Step()
    {
        var snapshot = player.Snapshot();
        if (Session is not { } session || Sources.Assets is not { } assets || _world is not { } world || !snapshot.Present || snapshot.Loading) return;
        if (!snapshot.Alive) { ClearOnDeath(); return; }
        _deathCleared = false;
        Vector3 origin = actions.Muzzle(session.Selected, HeldViews);
        Vector3 aim = actions.Aim(session.Selected, origin) - origin;
        bool admitted = !snapshot.Scripted && !snapshot.WrenchActive && !snapshot.WrenchThrown && !snapshot.WaterHidden && !snapshot.QuickSelectOpen && assets.Available(session.Selected);
        bool wasWindup = session.CannonFrame?.State == Rac3ShockCannonState.Windup;
        bool trigger = snapshot.TriggerHeld && admitted && session.Hand.IsHolding(session.Selected);
        var frame = session.Advance(new(trigger, admitted, ToNumerics(origin), ToNumerics(aim)));
        Pose = new(frame.Effects.Any(effect => effect.Kind == "shot") || !wasWindup && session.CannonFrame?.State == Rac3ShockCannonState.Windup, trigger);
        var contacts = frame.Queries.SelectMany(query => actions.Trace(query, origin)).ToArray();
        foreach (var intent in session.ResolveContacts(frame, contacts))
            if (actions.Apply(intent)) { AcceptedHits++; LastDamage = intent; }
        var presentation = world.Presentation;
        foreach (var cue in frame.Audio) presentation.QueueAudio(cue, session, assets);
        int previousAudio = presentation.AudioStarted;
        presentation.TickAudio(session.Tick, owner);
        AudioStarted += presentation.AudioStarted - previousAudio;
        foreach (var effect in frame.Effects.Where(effect => effect.Kind == "shot"))
        {
            player.Record("shot", new { effect.Shot });
            _modelPose = new(0, 2);
        }
        presentation.SynchronizeProjectiles(frame, session, assets, world.Root);
        if (frame.Queries.Any(query => query.Kind == WeaponQueryKind.Ray)) presentation.SpawnSpread(frame, contacts, world.Root);
        presentation.TickEffects();
        var status = session.Status(session.Selected);
        if (status == _displayedStatus && session.Hand.Equipment == _displayedHand) return;
        player.RefreshHud();
        _displayedStatus = status;
        _displayedHand = session.Hand.Equipment;
    }

    internal void StepViews(Rac1PlayerAnimationPresentationController controller, PlayerAvatar? avatar)
    {
        var snapshot = player.Snapshot();
        if (_world is not { } world) return;
        _modelPose = world.Presentation.StepViews(controller, Session, Sources.Assets, avatar, snapshot.Alive,
            snapshot.WrenchActive || snapshot.WrenchThrown, snapshot.WaterHidden,
            () => actions.Muzzle(Session!.Selected, HeldViews), actions.Forward, value => new(-value.X, value.Z, value.Y), _modelPose);
    }

    internal void ResetHeldUse()
    {
        Pose = Pose with { TriggerHeld = false };
        StopAudio();
    }

    internal void StopAudio(bool freeImmediately = false) => _world?.Presentation.StopAudio(freeImmediately);
    internal void SetPaused(bool paused) => _world?.Presentation.SetPaused(paused);

    internal void ClearOnDeath()
    {
        if (_deathCleared) return;
        ClearTransients();
        _deathCleared = true;
    }

    internal void ClearTransients(bool freeImmediately = false)
    {
        StopAudio(freeImmediately);
        player.ClearNativeTransients(freeImmediately);
        Session?.ClearTransientState();
        _world?.Presentation.Clear(freeImmediately);
        Pose = default;
    }

    internal void ResetWorld(bool freeImmediately = false)
    {
        if (_world is { } world) { world.Retire(freeImmediately); return; }
        // Native world configuration also invokes its reset before a renderer
        // is bound. Preserve that native generation/cue boundary explicitly.
        ClearTransients(freeImmediately);
        if (!_persistentSession) Session = null;
        _fixture?.Dispose();
        _fixture = null;
    }

    private void Retire(WorldBinding world, bool freeImmediately)
    {
        if (!ReferenceEquals(_world, world)) return;
        try { ClearTransients(freeImmediately); }
        finally
        {
            _world = null;
            try { world.Presentation.ReleaseViews(freeImmediately); }
            finally
            {
                if (!_persistentSession) Session = null;
                _fixture?.Dispose();
                _fixture = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ResetWorld(freeImmediately: true); }
        finally
        {
            try { Sources.Dispose(); }
            finally { _notice.Dispose(); }
        }
    }

    private sealed class WorldBinding(FusionWeaponHost host, Node3D root) : IDisposable
    {
        private bool _retired;
        internal Node3D Root { get; } = root;
        internal FusionWeaponPresentationHost Presentation { get; } = new();
        internal void Retire(bool freeImmediately)
        {
            if (_retired) return;
            _retired = true;
            host.Retire(this, freeImmediately);
        }
        public void Dispose() => Retire(freeImmediately: false);
    }

    private static NVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
}
