using System.Collections.Immutable;
using System.Numerics;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;
using OBP.Fusion;

namespace OBP.Game.Diagnostics;

public sealed record Rac1TaxiDockDiagnostic(int Side, Vector3 Waypoint);
public sealed record Rac1TaxiHudRequestDiagnostic(Rac1TaxiNativeHudElementLease.HudCommandKind Kind, int SlotId, int Key, int InitializeAddress);
public sealed record Rac1TaxiStopDiagnostic(ActorInstanceKey Actor, Vector3 Position, Vector3? OutboundEnd,
    Vector3? ApproachCentre, int ApproachPoints, bool HoverPhases, bool ShuttleDock, int? OptionalCuboidSelector,
    ImmutableArray<Rac1TaxiDockDiagnostic> DockEnds, int? SourceHudMessage, Rac1TaxiHudRequestDiagnostic? HudRequest);
public sealed record Rac1TaxiCabDiagnostic(Vector3 Position, Vector3 Nose, Rac1MetropolisTaxiSession.TrafficPhase? Phase, uint CollisionLayer);
public sealed record Rac1TaxiPassengerDiagnostic(DiagnosticWorldSnapshot World, int Nodes, int Hulls, int Hovers,
    bool SessionPresent, bool Riding, bool Returning, int? PendingStop, int? HeroTargetState, bool Flying,
    bool RiderFrame, bool CameraWorldOwned, bool ProcessDisabled, bool Walking, bool VisualVisible,
    bool CameraTopLevel, Vector3 CameraRelative, Vector3? ExpectedPassengerPosition, bool CabBasisOrthonormal,
    bool BoardableAtPlayer, string HudLine);
public sealed record Rac1TaxiLandingDiagnostic(bool Supported, Vector3 Position);
public interface IRac1TaxiDiagnosticFixture
{
    Rac1TaxiPassengerDiagnostic Read();
    ImmutableArray<Rac1TaxiStopDiagnostic> ReadStops();
    Rac1TaxiCabDiagnostic ReadCab(ActorInstanceKey actor);
    double SourceDistanceSquared(ActorInstanceKey actor);
    Rac1TaxiLandingDiagnostic ProbeLanding(Vector3 waypoint);
    string ReadRetailText(int id);
    IDisposable PreservePresentation();
    void SetTimeScale(double value);
    void SuspendAtApproach(ActorInstanceKey actor);
    void ResumeProcess();
    void StageAtSource(ActorInstanceKey actor, bool reverseGroundingLift);
    bool RequestBoarding();
    void Abort();
}
