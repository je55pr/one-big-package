using System.Numerics;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "S1244", Justification = "Exact native damage constants and source inventory witnesses preserve the original acceptance comparisons.")]
public sealed class FusionWeaponRecordingScenario(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports, IFusionWeaponDiagnosticFixture fusion)
    : FusionWeaponScenarioSupport(context, ports, fusion)
{
    public async Task RunAsync()
    {
        try
        {
            for (int i = 0; i < 360 && Fusion.ReadStatus().WaitingForEntry; i++)
                await ProcessFrameAsync();
            Require(Fusion.ReadStatus().Present && Fusion.ReadStatus().Problems == 0, "fusion recording requires all three original sources");
            await WaitForGroundedAsync(Player, 180);
            await PhysicsFramesAsync(20);
            int openingAmmo = Fusion.ReadStatus().Isolated ? 10 : 20;
            Require(Ports.Equipment.Read().BombAmmo == openingAmmo, "fresh Bomb Glove ammo");
            await CaptureFusionAsync("ordinary-opening");
            await SelectFusionByInputAsync(0);
            await FusionFireInputAsync(16); // The ordinary entry still draws the glove through the native Circle handoff.
            Require(Ports.Equipment.Read().BombAmmo == openingAmmo - 1, "recorded Bomb Glove did not consume one round");
            await PhysicsFramesAsync(90);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(1);
            await PhysicsFramesAsync(30);
            await CaptureFusionAsync("lancer-idle");
            await FusionFireInputAsync(60);
            Require(Fusion.ReadSession().LancerAmmo < 40, "recorded Lancer did not fire");
            await CaptureFusionAsync("ordinary-lancer");
            await FusionCheckWrenchPersistenceAsync();
            await PhysicsFramesAsync(30);
            await FusionMarkAsync();
            await SelectFusionByInputAsync(2);
            await PhysicsFramesAsync(30);
            await CaptureFusionAsync("cannon-idle");
            await FusionFireInputAsync(90);
            Require(Fusion.ReadSession().CannonAmmo <= 9, "recorded Shock Cannon did not spread and charge");
            await CaptureFusionAsync("ordinary-cannon");
            await FusionCheckWrenchPersistenceAsync();
            await PhysicsFramesAsync(45);
            await FusionMarkAsync();
            Vector3 start = Player.Read().GlobalPosition;
            Driver.SetAxis(DiagnosticPhysicalAxis.LeftY, -1);
            await PhysicsFramesAsync(20);
            Driver.SetAxis(DiagnosticPhysicalAxis.LeftY, 0);
            await PhysicsFramesAsync(15);
            Require(Player.Read().GlobalPosition.DistanceTo(start) > .2f, $"movement did not recover after equipment selection: {start}->{Player.Read().GlobalPosition}; phase={Player.Read().Rac1MovementPhase}");
            Log($"[fusion-record] PASS ordinary opening/input, three weapons, native ammo, charge and movement; foreign hits={Fusion.ReadSession().AcceptedHits}");
            // The original stop-recording edge is consumed immediately; the driver lease releases F9 on disposal.
            FusionKey(DiagnosticPhysicalKey.F9, true);
        }
        catch (OperationCanceledException error) when (Driver.Lifetime.IsCancellationRequested && error.CancellationToken == Driver.Lifetime)
        {
            throw;
        }
        catch (Exception error)
        {
            LogError("[fusion-record] FAIL: " + error);
            throw;
        }
    }


}
