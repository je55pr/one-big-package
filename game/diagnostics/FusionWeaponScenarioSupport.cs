using System.Collections.Immutable;
using System.Numerics;
using OBP.Core;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public abstract class FusionWeaponScenarioSupport(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports,
    IFusionWeaponDiagnosticFixture fusion) : Rac1WeaponScenarioSupport(context, ports)
{
    protected IFusionWeaponDiagnosticFixture Fusion { get; } = fusion;
    protected SourceWeaponKey Selected => Fusion.ReadSession().Selected ?? throw new DiagnosticUnavailableException("No shared weapon is selected.");
    protected void FusionKey(DiagnosticPhysicalKey key, bool pressed) => Driver.SetKey(key, pressed);
    protected Task CaptureFusionAsync(string name) => Fusion.CaptureAsync(name, false, Driver.Lifetime);
    protected Task CaptureFusionProfileAsync(string name) => Fusion.CaptureAsync(name, true, Driver.Lifetime);
    protected async Task WaitForSharedWeaponsSmokeAsync()
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (Fusion.ReadStatus().Preparing && timeout.Elapsed < TimeSpan.FromMinutes(4)) await ProcessFrameAsync();
        Require(Fusion.ReadStatus().AssetsPresent, "shared preparation did not finish: " + Fusion.ReadStatus().Failure);
    }
    protected async Task SelectFusionByInputAsync(int slot)
    {
        FusionKey(DiagnosticPhysicalKey.Q, true);
        for (int i = 0; i < 3; i++) await ProcessFrameAsync();
        Require(Fusion.ReadMenu().Open && Fusion.ReadMenu().Entries.Length == 3, "fusion Quick Select did not expose three entries");
        float angle = slot * MathF.PI / 4;
        Driver.SetAxis(DiagnosticPhysicalAxis.LeftX, MathF.Sin(angle));
        Driver.SetAxis(DiagnosticPhysicalAxis.LeftY, -MathF.Cos(angle));
        for (int i = 0; i < 4; i++) await ProcessFrameAsync();
        Require(Fusion.ReadMenu().Selected == FusionWeaponSession.Weapons[slot], "wheel direction selected the wrong source key");
        await CaptureFusionAsync("selector-" + slot);
        FusionKey(DiagnosticPhysicalKey.Q, false);
        Driver.SetAxis(DiagnosticPhysicalAxis.LeftX, 0); Driver.SetAxis(DiagnosticPhysicalAxis.LeftY, 0);
        await PhysicsFramesAsync(3);
        Require(!Fusion.ReadMenu().Open && Selected == FusionWeaponSession.Weapons[slot], "release did not equip the selected source");
    }
    protected async Task FusionFireInputAsync(int frames)
    { FusionKey(DiagnosticPhysicalKey.E, true); await PhysicsFramesAsync(frames); FusionKey(DiagnosticPhysicalKey.E, false); await PhysicsFramesAsync(2); }
    protected async Task FusionMarkAsync()
    { FusionKey(DiagnosticPhysicalKey.F8, true); await PhysicsFramesAsync(1); FusionKey(DiagnosticPhysicalKey.F8, false); }
    protected void RequireFusionGunAlignment()
    {
        var basis = Fusion.ReadHeld(Selected).Global; var animation = Fusion.ReadAnimation();
        Require(animation.UseSequence is null && animation.UseWeight is 0, $"{Selected} retained a firing pose while idle");
        RequireFusionHandAttachment();
        Require(MathF.Abs(basis.X.Length() - 1) < .0001f && MathF.Abs(basis.Y.Length() - 1) < .0001f &&
            MathF.Abs(basis.Z.Length() - 1) < .0001f && MathF.Abs(Vector3.Dot(basis.X, basis.Y)) < .0001f &&
            MathF.Abs(Vector3.Dot(basis.Y, basis.Z)) < .0001f && MathF.Abs(Vector3.Dot(basis.X, basis.Z)) < .0001f,
            $"{Selected} mount distorts the source proportions");
    }
    protected void RequireFusionHandAttachment()
    {
        var held = Fusion.ReadHeld(Selected); var expected = held.ExpectedHand;
        Require(expected is not null, $"{Selected} has no evaluated source hand");
        Require(held.Local.Origin.DistanceTo(expected!.Origin) < .00001f, $"{Selected} is detached from the native hand origin");
        Require(expected.X.DistanceTo(held.Local.X) < .00001f && expected.Y.DistanceTo(held.Local.Y) < .00001f &&
            expected.Z.DistanceTo(held.Local.Z) < .00001f, $"{Selected} axes do not follow the animated hand");
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification = "The exact zero blend reset is an original native animation transition witness.")]
    protected void RequireFusionTransitionContinuity(DiagnosticWeaponTransform previous, ImmutableArray<double>? previousSkin, ref int boundaries)
    {
        var animation = Fusion.ReadAnimation();
        if (!animation.Blending || animation.BlendFraction != 0 || previousSkin is null) return;
        boundaries++; var actual = Fusion.ReadHeld(Selected).Local;
        Require(actual.Origin.DistanceTo(previous.Origin) < .00001f && actual.X.DistanceTo(previous.X) < .00001f &&
            actual.Y.DistanceTo(previous.Y) < .00001f && actual.Z.DistanceTo(previous.Z) < .00001f, $"{Selected} gun snapped at a transition boundary");
        var skin = animation.Skin ?? throw new DiagnosticUnavailableException("The source skin pose is unavailable.");
        Require(skin.Zip(previousSkin.Value, (a, b) => Math.Abs(a - b)).Max() < .00001, $"{Selected} arms/body did not retain the rendered source pose");
    }
    protected async Task CheckFusionJumpAttachmentAsync(bool moving)
    {
        var key = Selected; await WaitForGroundedAsync(Player, 120);
        SetAnalogueSmokeInput(0, moving ? 1 : 0); await PhysicsFramesAsync(moving ? 12 : 2);
        var initial = Fusion.ReadHeld(key).Local; bool sawJump = false, rotated = false; int boundaries = 0;
        FusionKey(DiagnosticPhysicalKey.Space, true);
        for (int tick = 0; tick < 65; tick++)
        {
            if (tick == 16) FusionKey(DiagnosticPhysicalKey.Space, false);
            var previous = Fusion.ReadHeld(key).Local; var previousSkin = Fusion.ReadAnimation().Skin;
            await PhysicsFramesAsync(1); RequireFusionHandAttachment(); RequireFusionTransitionContinuity(previous, previousSkin, ref boundaries);
            if (Fusion.ReadAnimation().TraversalSequence is 7 or 8)
            {
                sawJump = true; var current = Fusion.ReadHeld(key).Local;
                rotated |= initial.X.DistanceTo(current.X) > .01f || initial.Z.DistanceTo(current.Z) > .01f;
                if (tick == 15) await CaptureFusionAsync($"{SourceIdentityWire.FormatSource(key.SourceGame)}-{(moving ? "moving" : "standing")}-jump");
            }
        }
        FusionKey(DiagnosticPhysicalKey.Space, false); ClearMovementSmokeInput(); await WaitForGroundedAsync(Player, 180);
        Require(sawJump && rotated, $"{key} {(moving ? "moving" : "standing")} jump retained locked weapon axes");
        Require(boundaries >= 1, $"{key} jump did not exercise a pose transition");
        Log($"[fusion-smoke] {key} {(moving ? "moving" : "standing")} jump hand position and axes PASS");
    }
    protected async Task CheckFusionMovementTransitionsAsync()
    {
        int boundaries = 0; var sequences = new HashSet<int>();
        foreach (var phase in new (bool Move, int Ticks)[] { (false, 20), (true, 45), (false, 90), (true, 45), (false, 6), (true, 30), (false, 90) })
        {
            SetAnalogueSmokeInput(0, phase.Move ? 1 : 0);
            for (int tick = 0; tick < phase.Ticks; tick++)
            {
                var previous = Fusion.ReadHeld(Selected).Local; var skin = Fusion.ReadAnimation().Skin;
                await PhysicsFramesAsync(1); RequireFusionHandAttachment(); RequireFusionTransitionContinuity(previous, skin, ref boundaries);
                if (Animation.Read().Sequence is int sequence) sequences.Add(sequence);
            }
        }
        ClearMovementSmokeInput();
        Require(boundaries >= 4 && sequences.Contains(0) && sequences.Any(s => s is 3 or 4) && sequences.Any(s => s is 5 or 6 or 20),
            $"{Selected} transition coverage incomplete: {boundaries} boundaries, sequences {string.Join(',', sequences)}");
        Log($"[fusion-smoke] {Selected} idle/run/skid/stop and interrupted transitions PASS ({boundaries} boundaries)");
    }
    protected async Task FusionCheckWrenchPersistenceAsync()
    {
        var key = Selected; int ammo = Fusion.ReadAmmo(key);
        FusionKey(DiagnosticPhysicalKey.X, true);
        await Rac1SmokeWaitAsync(PlayerWrenchActive, 20, "foreign equipment Square handoff");
        FusionKey(DiagnosticPhysicalKey.X, false);
        await Rac1SmokeWaitAsync(() => !PlayerWrenchActive(), 120, "foreign equipment Square recovery");
        await PhysicsFramesAsync(30);
        Require(Fusion.ReadSession().WrenchHeld && Ports.Bomb.ReadView().WrenchVisible && !Fusion.ReadHeld(key).Visible &&
            Selected == key && Fusion.ReadAmmo(key) == ammo, $"{key} did not retain the wrench and selected equipment independently");
        await CaptureFusionAsync(SourceIdentityWire.FormatSource(key.SourceGame) + "-wrench-retained");
        await FusionFireInputAsync(1); await PhysicsFramesAsync(2);
        Require(Fusion.ReadSession().Held == key && Fusion.ReadHeld(key).Visible && !Ports.Bomb.ReadView().WrenchVisible, $"{key} was not drawn by Circle");
        Log($"[fusion-smoke] {key} persistent wrench hand and Circle redraw PASS");
    }
    protected bool PlayerWrenchActive() => Fusion.ReadSession().WrenchActive;
}
