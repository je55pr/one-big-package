using System.Collections.Immutable;
using System.Numerics;
using OBP.Runtime.Gameplay;
using OBP.Fusion;
using OBP.RAC1.Gameplay;

namespace OBP.Game.Diagnostics;

public sealed record Rac1NovalisRockDiagnostic(ActorInstanceKey Actor, int State, Vector3 Position, Vector3 Home);
public sealed record Rac1NovalisPistonDiagnostic(ActorInstanceKey Actor, int End, int Timer, Vector3 Position);
public sealed record Rac1NovalisDoorDiagnostic(ActorInstanceKey Actor, int State, float Progress, Vector3 Centre, Vector3 Position);
public sealed record Rac1RouteRayDiagnostic(bool Hit, int? InstanceIndex, int? NativeClass, Vector3 Position);
public interface IRac1NovalisRouteFixture
{
    int MechanismCount { get; }
    ImmutableArray<Rac1NovalisRockDiagnostic> ReadRocks();
    Rac1NovalisPistonDiagnostic ReadPiston(int instanceIndex);
    Rac1NovalisDoorDiagnostic ReadDoor(int instanceIndex);
    Rac1RouteRayDiagnostic ProbePiston(ActorInstanceKey actor);
    Rac1RouteRayDiagnostic ProbeDoor(ActorInstanceKey actor);
}
public sealed record Rac1NovalisMusicTrackDiagnostic(int Slot, uint Sector, double DurationSeconds);
public sealed record Rac1NovalisMusicDiagnostic(DiagnosticWorldSnapshot World, ImmutableArray<Rac1NovalisMusicTrackDiagnostic> Tracks,
    Rac1NovalisMusic.Area? Area, int Slot, double IntroRemaining, int DecodedClips, ImmutableArray<string> MixerEvents, string AudioStatus);
public interface IRac1NovalisMusicFixture
{
    Rac1NovalisMusicDiagnostic Read();
    void SelectAuthoredCrank(int instanceIndex);
    void Tick(double delta);
}
