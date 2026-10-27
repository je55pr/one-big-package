using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using OBP.PS2;
using OBP.RAC1.Player;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal sealed class GodotFusionWeaponDiagnosticBindings
{
    internal required Func<FusionWeaponDiagnosticStatus> Status { get; init; }
    internal required Func<FusionWeaponSessionDiagnostic> Session { get; init; }
    internal required Func<FusionWeaponTransientDiagnostic> Transients { get; init; }
    internal required Func<FusionWeaponMenuDiagnostic> Menu { get; init; }
    internal required Func<FusionWeaponSession?> NativeSession { get; init; }
    internal required Func<FusionWeaponAssets?> Assets { get; init; }
    internal required Action<FusionWeaponAssets?> ReplaceAssets { get; init; }
    internal required Func<FusionWeaponPreparation?> Preparation { get; init; }
    internal required Func<ObpSourceLibrary> Sources { get; init; }
    internal required Action<ObpSourceLibrary> ReplaceSources { get; init; }
    internal required Func<ImmutableArray<KeyValuePair<SourceWeaponKey, FusionMeshView>>> Views { get; init; }
    internal required Func<IPlayerAnimationPresentationController?> Animation { get; init; }
    internal required Func<PlayerAvatar?> Avatar { get; init; }
    internal required Func<string> OriginalInventory { get; init; }
    internal required Func<string> CurrentInventory { get; init; }
    internal required Action Prepare { get; init; }
    internal required Action Reset { get; init; }
    internal required Action Clear { get; init; }
    internal required Func<bool> Reenter { get; init; }
    internal required Action ShowDestinations { get; init; }
    internal required Func<Camera3D> Camera { get; init; }
    internal required Func<PlayerHost?> Player { get; init; }
    internal required Func<NVector3> Forward { get; init; }
    internal required Func<int?> SourceSequence { get; init; }
    internal required Func<SourceWeaponKey?> Held { get; init; }
}
internal sealed class GodotFusionWeaponDiagnosticFixture(Node owner, GodotFusionWeaponDiagnosticBindings bindings,
    IDiagnosticFrameClock clock) : IFusionWeaponDiagnosticFixture
{
    private Rac1PlayerAnimationPresentationController? Controller => bindings.Animation() as Rac1PlayerAnimationPresentationController;
    internal static DiagnosticWeaponTransform Copy(Transform3D value) => new(GodotMovementPlayer.Copy(value.Origin),
        GodotMovementPlayer.Copy(value.Basis.X), GodotMovementPlayer.Copy(value.Basis.Y), GodotMovementPlayer.Copy(value.Basis.Z));
    public FusionWeaponDiagnosticStatus ReadStatus() => bindings.Status();
    public FusionWeaponSessionDiagnostic ReadSession() => bindings.Session();
    public FusionWeaponTransientDiagnostic ReadTransients() => bindings.Transients();
    public FusionWeaponMenuDiagnostic ReadMenu() => bindings.Menu();
    public int HeldModelCount => bindings.Views().Length;
    public int ReadAmmo(SourceWeaponKey weapon) => (bindings.NativeSession() ?? throw new DiagnosticUnavailableException("The shared weapon session is unavailable.")).Status(weapon).Ammo;
    public string OriginalInventory() => bindings.OriginalInventory();
    public string CurrentInventory() => bindings.CurrentInventory();
    public void PrepareSources() => bindings.Prepare();
    public void ResetWorldWeapons() => bindings.Reset();
    public void ClearTransients() => bindings.Clear();
    public bool ReenterWithoutIsolatedLoadout() => bindings.Reenter();
    public void ShowDestinations() => bindings.ShowDestinations();
    public FusionWeaponAnimationDiagnostic ReadAnimation()
    {
        var controller = Controller;
        var pose = controller?.Current.SampledPose;
        return new(controller?.SourceEquipmentUseSequence, controller?.SourceEquipmentUseWeight,
            controller?.SourceEquipmentIsBlending ?? false, controller?.SourceEquipmentBlendFraction ?? 1,
            controller?.TraversalSample?.Sequence, pose is null ? null : ImmutableArray.CreateRange(pose.LocalPositions));
    }
    public FusionWeaponHeldDiagnostic ReadHeld(SourceWeaponKey weapon)
    {
        var view = bindings.Views().SingleOrDefault(pair => pair.Key == weapon).Value;
        if (view is null || !GodotObject.IsInstanceValid(view)) throw new DiagnosticUnavailableException("The selected source weapon has no held model.");
        var hand = Controller?.CurrentAttachmentPose(PlayerEquipmentPose.HandAttachmentKey); var avatar = bindings.Avatar();
        DiagnosticWeaponTransform? expected = null;
        if (hand is not null && avatar is not null)
        {
            Vector3 Point(double x, double y, double z) => OBP.Godot.Player.PlayerAvatarView.ToGodotLocal(hand.Transform(new(x, y, z)), avatar.Axes, avatar.BaseHeight, true);
            var origin = Point(0, 0, 0);
            expected = Copy(new(new Basis(Point(-1, 0, 0) - origin, Point(0, 0, 1) - origin, Point(0, 1, 0) - origin).Orthonormalized(), origin));
        }
        return new(weapon, view.Visible, view.GetChildren().OfType<MeshInstance3D>().Any(), Copy(view.Transform), Copy(view.GlobalTransform), expected);
    }
    public IFusionAssetDiagnosticWitness CaptureAssets() => new AssetWitness(bindings, bindings.Assets()
        ?? throw new DiagnosticUnavailableException("Shared weapon assets are unavailable."));
    private sealed class AssetWitness(GodotFusionWeaponDiagnosticBindings bindings, FusionWeaponAssets assets) : IFusionAssetDiagnosticWitness
    { public bool IsCurrent => ReferenceEquals(bindings.Assets(), assets); }
    public IDisposable UseUnavailableAssets()
    {
        var previous = bindings.Assets(); var missing = new FusionWeaponAssets(new FusionWeaponData([]));
        try { bindings.ReplaceAssets(missing); }
        catch { missing.Dispose(); throw; }
        return new MovementFixtureLease(() =>
        {
            try { if (ReferenceEquals(bindings.Assets(), missing)) bindings.ReplaceAssets(previous); }
            finally { missing.Dispose(); }
        });
    }
    public IFusionSourceIsolation IsolateSources() => new SourceIsolation(bindings);
    private sealed class SourceIsolation(GodotFusionWeaponDiagnosticBindings bindings) : IFusionSourceIsolation
    {
        private readonly ObpSourceLibrary _original = bindings.Sources();
        private ObpSourceLibrary? _installed;
        private bool _disposed;
        public void DetachAll()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _installed = new(TrilogySourceDefinitions.All); bindings.ReplaceSources(_installed);
        }
        public void RestoreAll()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ReferenceEquals(bindings.Sources(), _original)) return;
            if (!ReferenceEquals(bindings.Sources(), _installed)) throw new DiagnosticUnavailableException("Another owner replaced the isolated source library.");
            bindings.ReplaceSources(_original); _installed = null;
        }
        public void Dispose() { if (_disposed) return; try { RestoreAll(); } finally { _disposed = true; } }
    }
    public IFusionPreparationDiagnosticWitness CapturePreparation() => new PreparationWitness(bindings,
        bindings.Preparation() ?? throw new DiagnosticUnavailableException("No shared preparation is active."));
    private sealed class PreparationWitness(GodotFusionWeaponDiagnosticBindings bindings, FusionWeaponPreparation preparation) : IFusionPreparationDiagnosticWitness
    {
        public bool IsCurrent => ReferenceEquals(bindings.Preparation(), preparation);
        public async Task RequireCanceledAsync(CancellationToken token)
        {
            try { await preparation.Completion.WaitAsync(token); }
            catch (OperationCanceledException) when (preparation.Completion.IsCanceled) { return; }
            throw new InvalidOperationException("Replaced source preparation was not canceled.");
        }
    }
    public async Task CaptureAsync(string name, bool profile, CancellationToken token)
    {
        string? directory = System.Environment.GetEnvironmentVariable("OBP_FUSION_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || DisplayServer.GetName() == "headless" || profile && ReadStatus().PlaytestPresent) return;
        Directory.CreateDirectory(directory);
        var camera = bindings.Camera(); var saved = camera.GlobalTransform; bool paused = owner.GetTree().Paused;
        try
        {
            if (profile)
            {
                owner.GetTree().Paused = true;
                var player = bindings.Player() ?? throw new DiagnosticUnavailableException("The profile capture player is unavailable.");
                var forward = GodotMovementPlayer.Engine(bindings.Forward());
                var target = player.GlobalPosition + Vector3.Up * .85f + forward * .3f;
                camera.GlobalPosition = target + forward.Cross(Vector3.Up) * 2.2f - forward * .5f + Vector3.Up * .4f;
                camera.LookAt(target);
            }
            await clock.NextAsync(DiagnosticFramePhase.PostDraw, token);
            using var image = owner.GetViewport().GetTexture().GetImage();
            if (image.SavePng(Path.Combine(directory, name + ".png")) != Error.Ok) throw new IOException("Fusion capture failed.");
            var animation = Controller; var forwardState = bindings.Forward();
            File.WriteAllText(Path.Combine(directory, name + ".pose.json"), JsonSerializer.Serialize(new
            {
                selected = ReadSession().Selected?.ToString(),
                heldEquipment = bindings.Held()?.ToString() ?? "wrench",
                sourceSequence = bindings.SourceSequence(),
                sourceUseSequence = animation?.SourceEquipmentUseSequence,
                sourceUseWeight = animation?.SourceEquipmentUseWeight,
                sourceBlendFraction = animation?.SourceEquipmentBlendFraction,
                forward = new[] { forwardState.X, forwardState.Y, forwardState.Z },
                weapons = bindings.Views().Select(pair => new
                {
                    key = pair.Key.ToString(),
                    pair.Value.Visible,
                    transform = new[] { pair.Value.GlobalBasis.X, pair.Value.GlobalBasis.Y, pair.Value.GlobalBasis.Z, pair.Value.GlobalPosition }.Select(v => new[] { v.X, v.Y, v.Z }),
                    localBounds = pair.Value.GetChildren().OfType<MeshInstance3D>().Select(mesh => new
                    { position = new[] { mesh.GetAabb().Position.X, mesh.GetAabb().Position.Y, mesh.GetAabb().Position.Z }, size = new[] { mesh.GetAabb().Size.X, mesh.GetAabb().Size.Y, mesh.GetAabb().Size.Z } })
                })
            }));
        }
        finally
        {
            if (profile)
            {
                if (GodotObject.IsInstanceValid(camera)) camera.GlobalTransform = saved;
                owner.GetTree().Paused = paused;
            }
        }
    }
}
