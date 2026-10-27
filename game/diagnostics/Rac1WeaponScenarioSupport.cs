using System.Numerics;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1WeaponScenarioPorts(IDiagnosticControllerEquipment Equipment, IRac1WeaponTargetFixture Targets,
    IRac1CombatDiagnosticFixture Combat, IWeaponDiagnosticCapture Capture, IRac1BombAcceptanceFixture Bomb,
    IWeaponMovementStateFixture Reproduction, IRac1AdditionalWeaponDiagnosticFixture Additional, IRac1ArsenalDiagnosticFixture Arsenal);
public abstract class Rac1WeaponScenarioSupport(MovementDiagnosticContext context, Rac1WeaponScenarioPorts ports) : MovementScenarioSupport(context)
{
    protected Rac1WeaponScenarioPorts Ports { get; } = ports;
    protected IRac1AdditionalWeaponDiagnosticFixture Additional => Ports.Additional;
    protected IRac1ArsenalDiagnosticFixture Arsenal => Ports.Arsenal;
    protected IRac1WeaponTargetFixture Targets => Ports.Targets;
    protected Vector3 EquipmentForward => Additional.Read().Forward;
    protected static SourceWeaponKey Weapon(Rac1WeaponId item) => new(ObpSourceGame.Rac1, (int)item);
    protected Task CaptureRac1BombAsync(string name, Vector3 focus, bool close = false) =>
        Ports.Capture.CaptureAsync(WeaponDiagnosticCaptureKind.Bomb, name, focus, close, Driver.Lifetime);
    protected static Vector3 FromEffectNative(Vector3 native) => new(-native.X, native.Z, native.Y);
}
