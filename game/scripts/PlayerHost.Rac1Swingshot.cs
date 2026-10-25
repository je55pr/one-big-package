using Godot;
using OBP.RAC1.Player;
using OBP.Runtime;

namespace OneBigPackage;

public partial class PlayerHost
{
    private Rac1TraversalEquipmentSwingshotAuthoredTarget[] _rac1TetherTargets = [];
    private Rac1TraversalEquipmentSwingshotAuthoredTarget? _rac1Tether;
    private Rac1TraversalEquipmentSwingAuthoredTarget[] _rac1SwingTargets = [];
    private Rac1TraversalEquipmentSwingAuthoredTarget? _rac1SwingTarget;
    private Rac1TraversalEquipmentSwing? _rac1Swing;
    private bool _rac1SwingLoop;
    private Rac1NativeVector3 _rac1TetherTranslation;
    private readonly Rac1TraversalEquipmentPull _rac1Pull = new();
    private double _rac1TetherRadius;
    private long _rac1TetherGeneration;
    private Rac1NativeVector3 _rac1TetherContact;
    private Rac1NativeVector3? _rac1TetherRequest;
    private Vector3 _rac1TetherStart;
    private MeshInstance3D? _rac1TetherLine;
    private readonly ImmediateMesh _rac1TetherMesh = new();

    public bool Rac1SwingshotEquipped { get; set; }
    public int? Rac1SwingshotTargetIndex => _rac1Tether?.InstanceIndex ?? _rac1SwingTarget?.InstanceIndex;
    public bool Rac1SwingshotActive => _rac1Tether is not null || _rac1SwingTarget is not null;
    public bool HasRac1SwingshotTargets => _rac1TetherTargets.Length != 0 || _rac1SwingTargets.Length != 0;
    private int Rac1SwingshotNativeAction => _rac1SwingTarget is not null ? 0x2c :
        _rac1Tether?.Target.Mode == Rac1TraversalEquipmentSwingshotMode.Swing ? 0x26 : 0x25;

    public void ConfigureRac1TraversalTargets(IEnumerable<RuntimeDynamicObject> objects)
    {
        var sources = objects.ToArray();
        _rac1TetherTargets = sources.Select(Rac1TraversalEquipmentSwingshotTarget.ReadAuthored)
            .Where(target => target.HasValue).Select(target => target!.Value).ToArray();
        _rac1SwingTargets = sources.Select(Rac1TraversalEquipmentSwingTarget.ReadAuthored)
            .Where(target => target.HasValue && !target.Value.Target.FollowsTargetFacing)
            .Select(target => target!.Value).ToArray();
    }

    private bool TryStepRac1Swingshot(Rac1MovementContext context)
    {
        _rac1TetherRequest = null;
        bool useHeld = !Scripted && _liveInput.TraversalHeld &&
            (Rac1DevelopmentSwingshotPreview || Rac1MovementEquipment.Swingshot && Rac1SwingshotEquipped);
        var point = ScenePlayerToNative(GlobalPosition);
        var position = new Rac1NativeVector3(point.X, point.Y, point.Z);
        if (!Rac1SwingshotActive && useHeld && !_rac1Ledge.Active)
        {
            double bestScore = double.PositiveInfinity;
            foreach (var candidate in _rac1TetherTargets)
            {
                bool lineOfSight = Rac1TetherLineOfSight(candidate.NativeAnchor);
                double? score = candidate.Target.CandidateScore(position, candidate.NativeAnchor,
                    _rac1Yaw.CurrentYaw, enabled: true, candidate.Target.HasUnrestrictedRegion, lineOfSight);
                if (score is not { } value || value >= bestScore) continue;
                bestScore = value;
                _rac1Tether = candidate;
            }
            foreach (var candidate in _rac1SwingTargets)
            {
                double? score = candidate.Target.CandidateScore(position, candidate.NativeAnchor,
                    _rac1Yaw.CurrentYaw, true, candidate.Target.HasUnrestrictedRegion,
                    Rac1TetherLineOfSight(candidate.NativeAnchor));
                if (score is not { } value || value >= bestScore) continue;
                bestScore = value;
                _rac1Tether = null;
                _rac1SwingTarget = candidate;
            }
            if (_rac1SwingTarget is { } pendulum)
            {
                _rac1TetherContact = new(-_rac1Movement.PlanarX, _rac1Movement.PlanarY, _rac1Movement.VerticalStep);
                _rac1TetherTranslation = _rac1TetherContact;
                _rac1Swing = new();
                _rac1Swing.Attach(pendulum.Target, position, pendulum.NativeAnchor, _rac1TetherContact);
                _rac1SwingLoop = false;
                _rac1Movement.CancelOrdinaryPlanarMotionForAction();
                _rac1TetherGeneration++;
                Rac1TraversalAnimationCycleCompleted = false;
            }
            if (_rac1Tether is { } selected)
            {
                // Development integration starts at the attached action. The
                // native held-gadget projectile/arrival lifecycle is separate.
                _rac1TetherRadius = Rac1TraversalEquipmentSwingshot.InitialSwingRadius(position, selected.NativeAnchor);
                _rac1TetherContact = new(-_rac1Movement.PlanarX, _rac1Movement.PlanarY, _rac1Movement.VerticalStep);
                _rac1Movement.CancelOrdinaryPlanarMotionForAction();
                _rac1Pull.Reset();
                _rac1TetherGeneration++;
            }
        }
        if (_rac1SwingTarget is { } swingTarget)
            return StepRac1Pendulum(swingTarget, position, useHeld, context);
        if (_rac1Tether is not { } target)
            return false;
        bool pull = target.Target.Mode == Rac1TraversalEquipmentSwingshotMode.Pull;
        if (!useHeld || pull && _rac1Pull.EvaluateExit(useHeld, context.GroundClearance) is not null)
        {
            _rac1Movement.ResumeAfterTraversal(-_rac1TetherContact.X, _rac1TetherContact.Y, _rac1TetherContact.Z);
            _rac1Tether = null;
            _rac1TetherMesh.ClearSurfaces();
            return false;
        }
        double facing = Math.Atan2(target.NativeAnchor.Y - position.Y, target.NativeAnchor.X - position.X);
        _rac1Yaw.Reset(facing);
        UpdateRac1FacingPresentation();
        Rac1NativeVector3 request;
        if (pull)
            request = _rac1Pull.Step(position, target.NativeAnchor, _rac1TetherContact.Length, facing);
        else
        {
            var swing = Rac1TraversalEquipmentSwingshot.StepSwing(position, target.NativeAnchor,
                _rac1TetherContact, _rac1TetherRadius, target.Target.Radius);
            _rac1TetherRadius = swing.Radius;
            request = swing.Request;
        }
        _rac1TetherRequest = request;
        _rac1TetherStart = GlobalPosition;
        _rac1PresentationGrounded = false;
        _locomotionAnimationInput = new(false, false, 0, Traversal: new(
            pull ? 0x25 : 0x26, 48, _rac1TetherGeneration, 0, pull ? 10 : 5, Loop: true));
        Velocity = LedgeNativeToScene(request) * 60f;
        DrawRac1Tether(target.NativeAnchor);
        return true;
    }

    private bool StepRac1Pendulum(Rac1TraversalEquipmentSwingAuthoredTarget target,
        Rac1NativeVector3 position, bool useHeld, Rac1MovementContext context)
    {
        if (_rac1Swing!.EvaluateExit(useHeld) is not null)
        {
            _rac1Movement.ResumeAfterTraversal(-_rac1TetherContact.X, _rac1TetherContact.Y, _rac1TetherContact.Z);
            _rac1SwingTarget = null;
            _rac1Swing = null;
            _rac1TetherMesh.ClearSurfaces();
            return false;
        }
        var analogue = Rac1AnalogueInput.ConditionUnitAxes(_liveInput.Move.X, -_liveInput.Move.Y);
        double desiredYaw = analogue.IsActive
            ? Rac1RatchetYawController.BuildMovementTarget(analogue.X, analogue.Y, context.ControlYaw)
            : _rac1Yaw.CurrentYaw;
        // Source swing-facing recurrence has a separate owner; this current
        // target-facing adapter leaves the recovered rope request independent.
        _rac1Yaw.Reset(_rac1Swing.EntryFacingYaw);
        UpdateRac1FacingPresentation();
        var swing = _rac1Swing.Step(position, target.NativeAnchor, _rac1TetherTranslation,
            _rac1Yaw.CurrentYaw, desiredYaw, analogue.Magnitude, context.GroundClearance, !IsOnFloor());
        var combined = swing.Request + swing.PositionCorrection;
        // Retail's direct radial correction and request are separate. Godot
        // sweeps both together to keep the preview from teleporting through walls.
        _rac1TetherRequest = combined;
        _rac1TetherStart = GlobalPosition;
        if (!_rac1SwingLoop && Rac1TraversalAnimationCycleCompleted)
        {
            _rac1SwingLoop = true;
            _rac1TetherGeneration++;
        }
        _rac1PresentationGrounded = false;
        _locomotionAnimationInput = new(false, false, 0, Traversal: new(0x2c,
            _rac1SwingLoop ? 53 : 52, _rac1TetherGeneration, _rac1SwingLoop ? 10 : 0,
            _rac1SwingLoop ? 12 : 11, Loop: _rac1SwingLoop,
            PlaybackMultiplier: _rac1SwingLoop ? .3f : .7f));
        Velocity = LedgeNativeToScene(combined) * 60f;
        DrawRac1Tether(target.NativeAnchor);
        return true;
    }

    private bool Rac1TetherLineOfSight(Rac1NativeVector3 anchor)
    {
        Vector3 from = GlobalPosition + Vector3.Up * .5f;
        Vector3 to = LedgeNativeToScene(anchor);
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 || hit.TryGetValue("position", out var point) && point.AsVector3().DistanceTo(to) < .15f;
    }

    private void RecordRac1TetherMotion()
    {
        if (_rac1TetherRequest is not { } request) return;
        Vector3 actual = GlobalPosition - _rac1TetherStart;
        _rac1TetherTranslation = new(-actual.X, actual.Z, actual.Y);
        _rac1TetherContact = Rac1ContactMotionProjection.Resolve(request, _rac1TetherTranslation);
    }

    private void DrawRac1Tether(Rac1NativeVector3 anchor)
    {
        if (_rac1TetherLine is null)
        {
            _rac1TetherLine = new MeshInstance3D { Mesh = _rac1TetherMesh, TopLevel = true };
            _rac1TetherLine.MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(.8f, .85f, 1f),
            };
            AddChild(_rac1TetherLine);
        }
        _rac1TetherMesh.ClearSurfaces();
        _rac1TetherMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        _rac1TetherMesh.SurfaceAddVertex(GlobalPosition + Vector3.Up * .7f);
        _rac1TetherMesh.SurfaceAddVertex(LedgeNativeToScene(anchor));
        _rac1TetherMesh.SurfaceEnd();
    }

    private void ResetRac1Tether()
    {
        _rac1Tether = null;
        _rac1SwingTarget = null;
        _rac1Swing = null;
        _rac1TetherTranslation = default;
        _rac1TetherRequest = null;
        _rac1TetherContact = default;
        _rac1Pull.Reset();
        _rac1TetherMesh.ClearSurfaces();
    }
}
