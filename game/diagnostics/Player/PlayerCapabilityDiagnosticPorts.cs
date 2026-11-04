using System.Collections.Immutable;
using System.Numerics;
using OBP.Fusion;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

public sealed record DiagnosticCapabilityFrame(long PhysicsTick, long ServiceTick, double Seconds, string? Sequence,
    int? Frame, ImmutableArray<double> LocalPositions, string AttachmentJson, double Facing, Vector3 Position, Vector3 Camera,
    int ShotsFired, long WrenchGeneration);
public interface IDiagnosticCapabilitySamples
{
    DiagnosticCapabilityFrame Read();
    IDisposable SubscribeAfterPhysics(Action sample);
}
public interface IDiagnosticCommonContactFixture
{
    IDisposable CreateDeck();
    void ResetPlayerOnDeck();
    MovementReproPlayer CaptureMovement();
    void RestoreMovement(MovementReproPlayer state);
}
public interface IDiagnosticRetiredActorProbe
{
    PlayerIntakeStatus? TryDamageRetiredActor();
    void RememberCurrentActor();
}
public sealed record DiagnosticCapabilityHealth(double Current, int SourceRac2Nanotech, double? HudCurrent,
    bool Alive, bool GameplayAlive, HudLifeState? HudLife, long Bolts);
public interface IDiagnosticCapabilityHealthFixture
{
    DiagnosticCapabilityHealth Read();
    bool StageFirstRac2HostileContact();
    void DefeatFromWorldHazard();
    bool Respawn();
}
