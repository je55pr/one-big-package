using Godot;
using OBP.Godot;
using OBP.RAC1.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    public bool Rac1EquippedItemHeld => !Scripted && !DevelopmentControls.FlyEnabled &&
        !Rac1Hanging && !Rac1SwingshotActive && !Rac1SwimmingActive && !Rac1DevelopmentSwingshotPreview && _liveInput.TraversalHeld;
    public Rac1MovementCapabilities Rac1MovementEquipment { get; set; }
    public Rac1TraversalEquipmentId? Rac1ActiveBackpack { get; set; }
    public bool Rac1ClankAvailable { get; set; }
    public int? Rac1DevelopmentPackPreview { get; private set; }
    public bool Rac1DevelopmentSwingshotPreview { get; private set; }
    public int? Rac1TraversalAction => Rac1SwimmingActive ? _rac1SwimState.NativeAction :
        Rac1SwingshotActive ? Rac1SwingshotNativeAction :
        Rac1Hanging ? _rac1Ledge.NativeAction : _rac1Movement.TraversalAction;

    private int ActiveRac1Pack => Rac1DevelopmentPackPreview ??
        (Rac1ActiveBackpack is { } pack && Rac1MovementEquipment.Owns(pack) ? (int)pack : 0);

    public void CycleRac1DevelopmentPack()
    {
        if (!UseRac1Gameplay) return;
        Rac1DevelopmentPackPreview = Rac1DevelopmentPackPreview switch
        {
            null => 2,
            2 => 3,
            _ => null,
        };
        GD.Print($"[player-dev] movement equipment preview: {Rac1EquipmentLabel}");
    }

    public void ToggleRac1DevelopmentSwingshot()
    {
        if (!UseRac1Gameplay) return;
        Rac1DevelopmentSwingshotPreview = !Rac1DevelopmentSwingshotPreview;
        GD.Print($"[player-dev] Swingshot preview {(Rac1DevelopmentSwingshotPreview ? "on" : "off")}");
    }

    public string Rac1EquipmentLabel => Rac1DevelopmentPackPreview switch
    {
        2 => "Heli-Pack preview",
        3 => "Thruster-Pack preview",
        _ => Rac1ActiveBackpack?.ToString() ?? "no aerial pack",
    };

    private Rac1MovementContext ProbeRac1MovementContext(double controlYaw)
    {
        var space = GetWorld3D().DirectSpaceState;
        var ground = PhysicsRayQueryParameters3D.Create(
            GlobalPosition + Vector3.Up * 0.02f, GlobalPosition + Vector3.Down * 64f);
        ground.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var groundHit = space.IntersectRay(ground);
        float clearance = groundHit.TryGetValue("position", out var groundPoint)
            ? Math.Max(0, GlobalPosition.Y - groundPoint.AsVector3().Y) : 64f;

        var facing = GetRac1NativePlanarBasis().Transform(Math.Cos(_rac1Yaw.CurrentYaw), Math.Sin(_rac1Yaw.CurrentYaw));
        Vector3 from = GlobalPosition + Vector3.Up * 0.7f;
        var forward = PhysicsRayQueryParameters3D.Create(from,
            from + new Vector3((float)facing.X, 0, (float)facing.Y) * 4f);
        forward.Exclude = ground.Exclude;
        var forwardHit = space.IntersectRay(forward);
        float forwardClearance = forwardHit.TryGetValue("position", out var forwardPoint)
            ? from.DistanceTo(forwardPoint.AsVector3()) : 4f;
        float normalAngle = forwardHit.TryGetValue("normal", out var normal)
            ? normal.AsVector3().AngleTo(Vector3.Up) : MathF.PI / 2f;
        bool crate = forwardHit.TryGetValue("collider", out var collider) &&
            collider.AsGodotObject() is Node node &&
            RuntimeWorldScene.FindDynamicObjectRoot(node) is { Source.NativeClassId: >= 500 and <= 540 };
        var context = new Rac1MovementContext(ActiveRac1Pack, Rac1ClankAvailable || Rac1DevelopmentPackPreview.HasValue,
            _rac1Yaw.CurrentYaw, controlYaw, clearance, forwardClearance, normalAngle, crate,
            NativePositionHeight: GlobalPosition.Y,
            NativeAnimationExitRequested: Rac1TraversalAnimationCycleCompleted,
            AuthoredAnimationTime: (float)Rac1TraversalAuthoredAnimationTime);
        return context with { WallProbe = ProbeRac1Wall(context) };
    }
}
