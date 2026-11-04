using Godot;
using OBP.Godot.Player;
using OBP.Runtime;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>
/// Taxi-facing boundary for passenger movement, controls and camera. The taxi
/// cannot mutate unrelated PlayerHost state or retain a global player owner.
/// </summary>
internal interface IRac1TaxiPassengerController
{
    bool Valid { get; }
    bool InsideTree { get; }
    Vector3 Position { get; set; }
    Vector3 Velocity { get; }
    double Yaw { get; }
    bool Idle { get; }
    bool Alive { get; }
    bool Scripted { get; }
    bool FlyEnabled { get; }
    bool RemoteControlActive { get; }
    bool Walking { get; }
    Node.ProcessModeEnum ProcessMode { get; set; }
    bool VisualVisible { get; set; }
    bool CameraTopLevel { get; }
    bool CameraValid { get; }
    Vector3 CameraPosition { get; set; }
    Rid BodyRid { get; }
    World3D World { get; }
    void BeginWalk(Vector3 target, double heading);
    void EndWalk();
    void Arrive(Vector3 scenePosition);
    void RestoreCamera();
}

/// <summary>Lease-local bridge to PlayerHost. A capture never outlives the scene owner.</summary>
internal sealed class Rac1TaxiPlayerController(PlayerHost player) : IRac1TaxiPassengerController
{
    public bool Valid => GodotObject.IsInstanceValid(player);
    public bool InsideTree => player.IsInsideTree();
    public Vector3 Position { get => player.GlobalPosition; set => player.GlobalPosition = value; }
    public Vector3 Velocity => player.Velocity;
    public double Yaw => player.Rac1CurrentYaw;
    public bool Idle => player.AnimationState == PlayerAnimationState.Idle;
    public bool Alive => player.Rac1GameplayAlive;
    public bool Scripted => player.Scripted;
    public bool FlyEnabled => player.DevelopmentControls.FlyEnabled;
    public bool RemoteControlActive => player.Rac1RemoteControlActive;
    public bool Walking => player.Rac1TaxiWalking;
    public Node.ProcessModeEnum ProcessMode { get => player.ProcessMode; set => player.ProcessMode = value; }
    public bool VisualVisible { get => player.VisualRoot.Visible; set => player.VisualRoot.Visible = value; }
    public bool CameraTopLevel => player.Camera.TopLevel;
    public bool CameraValid => GodotObject.IsInstanceValid(player.Camera);
    public Vector3 CameraPosition { get => player.Camera.GlobalPosition; set => player.Camera.GlobalPosition = value; }
    public Rid BodyRid => player.GetRid();
    public World3D World => player.GetWorld3D();
    public void BeginWalk(Vector3 target, double heading) => player.BeginRac1TaxiWalk(target, heading);
    public void EndWalk() => player.EndRac1TaxiWalk();
    public void Arrive(Vector3 scenePosition) => player.ApplyRac1TaxiArrival(
        new RuntimeSpawn(-scenePosition.X, scenePosition.Y, scenePosition.Z, player.Rac1CurrentYaw));
    public void RestoreCamera() => player.CenterCameraBehindPlayer();
}

internal sealed record Rac1TaxiHostPorts(
    Func<IRac1TaxiPassengerController?> Passenger,
    Func<bool> InputEnabled,
    Func<bool> Dead);
