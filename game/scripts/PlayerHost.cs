using Godot;
using OBP.Core.Math;
using OBP.Godot;
using OBP.Godot.Camera;
using OBP.Godot.Controls;
using OBP.Godot.Player;
using OBP.RAC1.Camera;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime;
using OBP.Runtime.Camera;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>
/// Godot host for ordinary Ratchet play against reconstructed world collision.
/// Normal grounded/airborne movement always uses the retail-derived R&amp;C1
/// controller as OBP's explicit cross-game trilogy default. That reuse is an OBP
/// design choice, not evidence that GC or UYA used the same native controller.
///
/// Godot owns collision, floor/ceiling contacts and scene-unit velocity. Recovered
/// R&amp;C1 camera snapshots can own ordinary camera pose and control heading through
/// the neutral camera contract; host camera fallback remains presentation policy.
/// Developer-only fly/noclip, diagnostics, camera override and spawn reset live in
/// <see cref="PlayerDevelopmentControls"/> rather than this production controller.
/// Headless capture can supply deterministic canned input.
/// </summary>
public partial class PlayerHost : CharacterBody3D
{
    // --- host presentation tuning ----------------------------------------------
    // Ordinary movement constants live in OBP.RAC1.Player. Mouse sensitivity is
    // host presentation policy; development-only tuning lives beside this host.
    public float MouseSensitivity { get; set; } = 0.0022f;

    // Development fallback only. Normal RAC1 camera state must come through
    // ApplyRac1CameraState; these values are not retail camera constants.
    public const float GamepadCameraYawRadiansPerSecond = 2.4f;
    public const float GamepadCameraPitchRadiansPerSecond = 2.0f;
    public const float GamepadCameraDeadzone = 0.12f;

    /// <summary>Third-person camera distance behind the capsule (pulled in when it would clip geometry).</summary>
    public const float CamDistance = 10f;

    /// <summary>Use the deterministic canned input instead of the real keyboard / mouse.</summary>
    public bool Scripted { get; set; }

    /// <summary>
    /// Upward reach used by the initial ground-snap ray. The legacy host default
    /// remains 8 units; source-game hosts may narrow it when an authored start
    /// sits beneath valid overhead collision.
    /// </summary>
    public float InitialGroundSnapUpwardReach { get; set; } = 8f;

    /// <summary>Keep scripted captures stationary after ground placement.</summary>
    public bool ScriptedStill { get; set; }

    /// <summary>
    /// Enable R&amp;C1-specific gameplay hooks such as native weapon selection and
    /// the recovered Veldin respawn session. Movement is retail-derived in every
    /// supported trilogy world regardless of this flag.
    /// </summary>
    public bool UseRac1Gameplay { get; set; }

    /// <summary>Deterministic label exposed in capture telemetry.</summary>
    public string MovementControllerLabel => "rac1-retail-derived-common-base";

    /// <summary>Explicit development-only control surface attached beside the production host.</summary>
    public PlayerDevelopmentControls DevelopmentControls { get; private set; } = null!;

    /// <summary>Current conditioned left-stick magnitude from the common R&amp;C1 controller.</summary>
    public double Rac1AnalogueMagnitude => _rac1Movement.AnalogueInput.Magnitude;

    /// <summary>Current native planar target step after analogue conditioning.</summary>
    public double Rac1TargetPlanarStep => _rac1Movement.TargetPlanarStep;

    /// <summary>Maximum Godot slide contacts consumed by one ordinary R&C1 movement tick since reset.</summary>
    public int Rac1MaxObservedSlideCollisions { get; private set; }

    /// <summary>Reset host collision diagnostics without changing player/controller state.</summary>
    public void ResetRac1CollisionDiagnostics() => Rac1MaxObservedSlideCollisions = 0;

    /// <summary>
    /// Non-R&C1 primary-action request. The GC debug crate harness and bounded
    /// UYA compatibility host may resolve contact; source-game gameplay code owns
    /// any admitted consequence.
    /// </summary>
    public event Action? CrateStrikeRequested;

    /// <summary>Normal R&amp;C1 primary attack input; the RAC1 host resolves the equipped item.</summary>
    public event Action? Rac1PrimaryAttackRequested;

    /// <summary>Host-only keyboard selection seam for the bounded R&amp;C1 weapon inventory.</summary>
    public event Action<Rac1WeaponId>? Rac1WeaponSelectionRequested;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Presentation-only root; replacing its visual never changes controller physics.</summary>
    public PlayerVisualRoot VisualRoot { get; private set; } = null!;

    /// <summary>Current engine-neutral presentation state, exposed for deterministic inspection.</summary>
    public PlayerAnimationState AnimationState => _animationStateMachine.State;

    /// <summary>
    /// Present the admitted ordinary wrench attack. Ranged item 10 deliberately
    /// does not call this seam: its recovered selector is sequence 44, while the
    /// only admitted PrimaryAttack playback clip is wrench sequence 23.
    /// </summary>
    public void NotifyRac1WrenchAttackAccepted()
    {
        if (!UseRac1Gameplay || !Rac1GameplayAlive)
            return;

        _attackRequested = true;
        _rac1Movement.CancelOrdinaryPlanarMotionForAction();
        _rac1WrenchMotion.Begin(_rac1Yaw.CurrentYaw);
    }

    /// <summary>Current R&amp;C1 native-space control/view yaw.</summary>
    public double Rac1ControlYaw => _rac1Yaw.ControlYaw;

    /// <summary>Current ordinary-movement yaw target after control-relative input mapping.</summary>
    public double Rac1MovementTargetYaw => _rac1Yaw.TargetYaw;

    /// <summary>Live native player yaw; combat facing reads this rather than camera/control yaw.</summary>
    public double Rac1CurrentYaw
    {
        get => _rac1Yaw.CurrentYaw;
        set
        {
            _rac1Yaw.Reset(value);
            UpdateRac1FacingPresentation();
        }
    }

    /// <summary>Live native yaw recurrence velocity in radians per update.</summary>
    public double Rac1YawVelocity => _rac1Yaw.YawVelocity;

    /// <summary>Retail-backed RAC1 locomotion state available to presentation code.</summary>
    public Rac1RatchetLocomotionState Rac1LocomotionState => _rac1Movement.LocomotionState;

    /// <summary>Recovered movement phase exposed for deterministic R&amp;C1 smoke assertions.</summary>
    public Rac1RatchetMovementPhase Rac1MovementPhase => _rac1Movement.Phase;

    /// <summary>Recovered support decision used by R&amp;C1 presentation for the current tick.</summary>
    public bool Rac1RecoveredSupportGrounded => _rac1PresentationGrounded;

    /// <summary>Retail-backed RAC1 yaw recurrence mode for deterministic inspection.</summary>
    public Rac1RatchetYawMode Rac1YawMode => _rac1Movement.YawMode;

    /// <summary>
    /// Latest evidence-backed surface action request. This is diagnostic only
    /// until the corresponding alternate native controller is implemented.
    /// </summary>
    public Rac1SurfaceActionIntent? Rac1SurfaceActionIntent => _rac1SurfaceActionIntent;

    /// <summary>Current engine-independent R&C1 player gameplay snapshot.</summary>
    public Rac1RatchetNanotechSnapshot? Rac1GameplayState { get; set; }

    /// <summary>Compatibility diagnostic derived from the gameplay snapshot, never host-owned state.</summary>
    public bool Rac1GameplayAlive => Rac1GameplayState is null || !Rac1GameplayState.IsDead;

    /// <summary>Whether the recovered player lifecycle admits ordinary CharacterBody movement.</summary>
    public bool Rac1GameplayAllowsOrdinaryMovement =>
        Rac1GameplayState is null || Rac1GameplayState.AllowsOrdinaryCharacterMovement;

    /// <summary>Unconditioned right-stick intent feeding the recovered type-0 producer or debug fallback.</summary>
    public Vector2 RawCameraIntent => _liveInput.CameraIntent;

    /// <summary>Current SDL/Godot controller diagnostic, when a joypad is connected.</summary>
    public RawGamepadDiagnostic? RawGamepadDiagnostic => _liveInput.Diagnostic;

    /// <summary>Which camera source currently owns ordinary presentation and control heading.</summary>
    public string CameraControllerLabel =>
        HasActiveRecoveredCamera ? "rac1-native-type0" : "host-debug-fallback";

    /// <summary>True when a recovered RAC1 camera snapshot is actively driving Godot.</summary>
    public bool HasActiveRecoveredCamera =>
        UseRac1Gameplay &&
        !(DevelopmentControls?.FlyEnabled ?? false) &&
        !(DevelopmentControls?.ForceHostCamera ?? false) &&
        _rac1RuntimeCameraState is not null;

    /// <summary>Latest recovered engine-neutral camera state, if one has been supplied.</summary>
    public RuntimeCameraState? Rac1RuntimeCameraState => _rac1RuntimeCameraState;

    /// <summary>Recovered type-0 horizontal manual-input state.</summary>
    public double Rac1CameraManualYaw => _rac1Camera.ManualYawState;

    /// <summary>Recovered type-0 vertical manual-input state.</summary>
    public double Rac1CameraManualPitch => _rac1Camera.ManualPitchState;

    /// <summary>Recovered obstruction radial correction and release timer.</summary>
    public double Rac1CameraObstructionCorrection => _rac1Camera.ObstructionCorrection;
    public int Rac1CameraObstructionReleaseTicks => _rac1Camera.ObstructionReleaseTicks;

    private Node3D _yaw = null!;
    private Node3D _pitch = null!;
    private Label _hud = null!;
    private float _cameraDistance = CamDistance;
    private double _time;
    private bool _landed;
    private bool _placed;
    private bool _scriptJumped;
    private int _placeTries;
    private PlayerAnimationStateMachine _animationStateMachine = new();
    private bool _animationGroundedInitialized;
    private bool _animationWasGrounded;
    private bool _rac1PresentationGrounded;
    private bool _attackRequested;
    private bool _scriptAttacked;
    private readonly Rac1RatchetMovementController _rac1Movement = new();
    private readonly Rac1WrenchMotionSession _rac1WrenchMotion = new();
    private readonly Rac1RatchetYawController _rac1Yaw = new();
    private readonly Rac1DynamicSupportSession _rac1DynamicSupport = new();
    private Rac1MobyRuntimeKey? _rac1HostSupportKey;
    private Vector3 _rac1HostSupportLocalAnchor;
    private bool _rac1HostSupportAnchorValid;
    private Rac1SurfaceActionIntent? _rac1SurfaceActionIntent;
    private readonly Rac1OrdinaryCameraController _rac1Camera = new();
    private readonly RawGamepadInput _rawInput = new();
    private RawPlayerInputFrame _liveInput;
    private bool _rac1JumpWasHeld;
    private RuntimeCameraState? _rac1RuntimeCameraState;
    private bool _rac1CameraInitialized;

    // last-jump measurement
    private bool _airborne;
    private Vector3 _jumpStart;
    private double _jumpStartTime;
    private float _jumpApexY;
    private string _lastJump = "-";

    public override void _Ready()
    {
        const float radius = 0.6f;
        const float height = 2.6f;

        DevelopmentControls = new PlayerDevelopmentControls { Name = "DevelopmentControls" };
        AddChild(DevelopmentControls);
        DevelopmentControls.Attach(this, GlobalPosition);

        AddChild(new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
            Position = new Vector3(0, height / 2f, 0),
        });

        VisualRoot = new PlayerVisualRoot { Name = "VisualRoot" };
        AddChild(VisualRoot);
        VisualRoot.ConfigureDebugFallback(radius, height);

        _yaw = new Node3D { Name = "Yaw" };
        AddChild(_yaw);
        // A scripted-capture camera sits higher, further back and near-level so
        // the showcase shot frames the planet's horizon, not the dirt underfoot.
        float pivotUp = Scripted ? height + 3.0f : height + 1.2f;
        _pitch = new Node3D { Name = "Pitch", Position = new Vector3(0, pivotUp, 0) };
        _pitch.RotationDegrees = new Vector3(Scripted ? -18f : -15f, 0, 0);
        _yaw.AddChild(_pitch);

        float camBack = Scripted ? 13f : CamDistance;
        _cameraDistance = camBack;
        Camera = new Camera3D { Name = "PlayerCamera", Position = new Vector3(0, 0, camBack), Far = 12000f, Near = 0.08f };
        _pitch.AddChild(Camera);
        Camera.MakeCurrent();

        var layer = new CanvasLayer { Name = "PlayerHud" };
        AddChild(layer);
        _hud = new Label { Position = new Vector2(16, 210) };
        _hud.AddThemeColorOverride("font_color", new Color(0.7f, 0.95f, 0.7f));
        layer.AddChild(_hud);

        // RAC1 supplies its own recovered 54/3600 downward contact request.
        // Do not stack Godot's arbitrary floor-snap distance on top of it.
        FloorSnapLength = UseRac1Gameplay ? 0f : 1.5f;
        // R&C1 now constructs the recovered directional-slope tangent itself;
        // do not ask Godot to invent a second constant-speed slope projection.
        FloorConstantSpeed = false;
        FloorMaxAngle = UseRac1Gameplay
            ? (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportMaxAngleRadians
            : Mathf.DegToRad(60f);
        // R&C1 retail neutral release reaches exact zero displacement on an
        // admitted non-flat Veldin face. This Godot flag is the host mechanism
        // for that witnessed no-drift behavior, not a claimed native boolean.
        FloorStopOnSlope = true;
        MaxSlides = 6;
        // Scene geometry is 1:1 with RAC1 world units. Godot's recovery margin is
        // not the native collision algorithm, but using the recovered ordinary
        // support contact-correction tolerance removes the old arbitrary 0.1u
        // RAC1 skin while preserving the sequel/debug-host boundary.
        SafeMargin = UseRac1Gameplay
            ? (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportContactMetricLimit
            : 0.1f;

        if (!Scripted)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Scripted)
        {
            return;
        }

        _rawInput.Observe(@event);

        if (@event is InputEventMouseMotion motion &&
            Input.MouseMode == Input.MouseModeEnum.Captured &&
            !HasActiveRecoveredCamera)
        {
            _yaw.RotateY(-motion.Relative.X * MouseSensitivity);
            float p = Mathf.Clamp(
                _pitch.Rotation.X - motion.Relative.Y * MouseSensitivity,
                Mathf.DegToRad(-82f), Mathf.DegToRad(55f));
            _pitch.Rotation = new Vector3(p, 0, 0);
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (UseRac1Gameplay && key.Keycode == Key.Key1)
            {
                Rac1WeaponSelectionRequested?.Invoke(Rac1WeaponId.Wrench);
            }
            else if (UseRac1Gameplay && key.Keycode == Key.Key2)
            {
                Rac1WeaponSelectionRequested?.Invoke(Rac1WeaponId.FirstRanged);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // The trimesh collision shapes take a physics frame or two to register;
        // retry the ground snap until it hits (or give up and free-fall).
        if (!_placed)
        {
            if (SnapToGroundBelow() || ++_placeTries > 20)
            {
                _placed = true;
            }
            else
            {
                return;
            }
        }

        _time += delta;
        if (UseRac1Gameplay && !DevelopmentControls.FlyEnabled)
            EnsureRac1CameraInitialized();

        if (!Scripted)
        {
            _liveInput = _rawInput.Read();
            if (!HasActiveRecoveredCamera)
                ApplyGamepadCamera((float)delta, _liveInput.CameraIntent);
            if (_liveInput.ActionJustPressed)
                RequestPrimaryAction();
        }

        if (DevelopmentControls.FlyEnabled)
        {
            _attackRequested = false;
            DevelopmentControls.StepFly((float)delta, _liveInput.Move);
            UpdateHud(true);
            return;
        }

        var (move, jump) = Scripted ? ScriptedInput() : (_liveInput.Move, _liveInput.JumpHeld);
        bool crouch = !Scripted && _liveInput.CrouchHeld;
        if (UseRac1Gameplay && !Rac1GameplayAllowsOrdinaryMovement)
        {
            _attackRequested = false;
            _rac1JumpWasHeld = false;
            Velocity = Vector3.Zero;
            UpdateHud(false);
            return;
        }

        StepRetailDerivedMovement(move, jump, crouch);
        MoveAndSlide();
        if (UseRac1Gameplay)
            Rac1MaxObservedSlideCollisions = Math.Max(
                Rac1MaxObservedSlideCollisions,
                GetSlideCollisionCount());
        bool isOnFloor = IsOnFloor();
        UpdateAnimationState(isOnFloor);

        if (!Scripted && !HasActiveRecoveredCamera)
        {
            UpdateCameraDistance();
        }

        if (UseRac1Gameplay && !DevelopmentControls.FlyEnabled)
            StepRac1Camera(Scripted ? Vector2.Zero : _liveInput.CameraIntent);

        TrackJump(isOnFloor);

        if (!_landed && isOnFloor)
        {
            _landed = true;
            GD.Print($"[PlayerHost] on the collision floor at {GlobalPosition} after {_time:0.00}s");
        }

        UpdateHud(isOnFloor);
    }

    private void EnsureRac1CameraInitialized()
    {
        if (_rac1CameraInitialized)
            return;

        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        double initialHeading = GetRac1ControlYaw();
        _rac1Camera.Reset(nativePlayer, initialHeading);
        var state = _rac1Camera.Step(new Rac1OrdinaryCameraController.Input(
            nativePlayer,
            0d,
            0d,
            default));
        _rac1CameraInitialized = true;
        ApplyRac1CameraState(state);
    }

    private void StepRac1Camera(Vector2 rawCameraIntent)
    {
        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        RuntimeCameraObstructionFacts obstruction = ProbeRac1CameraObstruction();
        var state = _rac1Camera.Step(new Rac1OrdinaryCameraController.Input(
            nativePlayer,
            rawCameraIntent.X,
            -rawCameraIntent.Y,
            obstruction));
        ApplyRac1CameraState(state);
    }

    private RuntimeCameraObstructionFacts ProbeRac1CameraObstruction()
    {
        // The exact retail contact primitive is still intentionally unpromoted.
        // Godot supplies only contact/orientation facts; RAC1 owns the response law.
        double heading = _rac1Camera.ControlHeading;
        Vector3 from = GlobalPosition + Vector3.Up * (float)Rac1OrdinaryCameraController.OrdinaryLookHeight;
        Vec3 nativePlayer = ScenePlayerToNative(GlobalPosition);
        var desiredEyeNative = new Vec3(
            nativePlayer.X - (Math.Cos(heading) * Rac1OrdinaryCameraController.OrdinaryPreferredRadius),
            nativePlayer.Y - (Math.Sin(heading) * Rac1OrdinaryCameraController.OrdinaryPreferredRadius),
            nativePlayer.Z + Rac1OrdinaryCameraController.OrdinaryEyeHeight);
        Vector3 to = RuntimeWorldScene.ToScene(
            desiredEyeNative.X,
            desiredEyeNative.Z,
            desiredEyeNative.Y);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
            return default;

        Vector3 normal = (Vector3)hit["normal"];
        Vector3 sceneRight = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
            Math.Sin(heading),
            -Math.Cos(heading));
        return new RuntimeCameraObstructionFacts(
            true,
            normal.Dot(sceneRight));
    }

    private static Vec3 ScenePlayerToNative(Vector3 scene) =>
        new(-scene.X, scene.Z, scene.Y);

    /// <summary>
    /// Supplies a recovered R&amp;C1 camera snapshot. When ordinary R&amp;C1 play
    /// owns the camera, this state drives both the visible pose and movement's
    /// control-relative heading/basis.
    /// </summary>
    public void ApplyRac1CameraState(Rac1CameraState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _rac1RuntimeCameraState = state.ToRuntimeState();
        if (HasActiveRecoveredCamera)
            ApplyRecoveredCameraPresentation();
    }

    /// <summary>Drop recovered camera ownership and return to the debug host camera.</summary>
    public void ClearRac1CameraState()
    {
        _rac1RuntimeCameraState = null;
        RestoreHostCameraPresentation();
    }

    private void ApplyRecoveredCameraPresentation()
    {
        if (_rac1RuntimeCameraState is null)
            return;
        Camera.TopLevel = true;
        RuntimeCameraSceneAdapter.Apply(Camera, _rac1RuntimeCameraState);
    }

    private void RestoreHostCameraPresentation()
    {
        Camera.TopLevel = false;
        Camera.Position = new Vector3(0f, 0f, _cameraDistance);
        Camera.Rotation = Vector3.Zero;
    }

    private double GetRac1ControlYaw()
    {
        if (HasActiveRecoveredCamera)
            return _rac1RuntimeCameraState!.ControlHeadingRadians;

        // Development fallback only. R&C1 owns how raw planar stick direction
        // combines with the supplied forward/control heading to form G+0x100.
        Vector3 sceneForward = _yaw.GlobalTransform.Basis * new Vector3(0f, 0f, -1f);
        sceneForward.Y = 0f;
        if (sceneForward.LengthSquared() <= 1e-8f) return _rac1Yaw.ControlYaw;
        sceneForward = sceneForward.Normalized();
        return Math.Atan2(sceneForward.Z, -sceneForward.X);
    }

    private PlayerPlanarBasis GetRac1PlanarBasis()
    {
        if (HasActiveRecoveredCamera)
        {
            double heading = _rac1RuntimeCameraState!.ControlHeadingRadians;
            Vector3 recoveredForward = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
                Math.Cos(heading),
                Math.Sin(heading)).Normalized();
            Vector3 recoveredRight = PlayerAvatarFacing.NativeZUpPlanarDirectionToGodot(
                Math.Sin(heading),
                -Math.Cos(heading)).Normalized();
            return new PlayerPlanarBasis(
                recoveredRight.X,
                recoveredRight.Z,
                recoveredForward.X,
                recoveredForward.Z);
        }

        Vector3 sceneRight = _yaw.GlobalTransform.Basis * Vector3.Right;
        Vector3 sceneForward = _yaw.GlobalTransform.Basis * new Vector3(0f, 0f, -1f);
        sceneRight.Y = 0f;
        sceneForward.Y = 0f;
        sceneRight = sceneRight.Normalized();
        sceneForward = sceneForward.Normalized();
        return new PlayerPlanarBasis(
            sceneRight.X,
            sceneRight.Z,
            sceneForward.X,
            sceneForward.Z);
    }

    private static PlayerPlanarBasis GetRac1NativePlanarBasis() =>
        new(-1d, 0d, 0d, 1d);

    private void UpdateRac1FacingPresentation()
    {
        if (VisualRoot is null) return;
        float sceneYaw = PlayerAvatarFacing.NativeZUpYawToGodotSceneYaw(_rac1Yaw.CurrentYaw);
        VisualRoot.Rotation = new Vector3(0f, sceneYaw - Rotation.Y, 0f);
    }

    /// <summary>
    /// Apply a recovered R&amp;C1 restart transform. RuntimeSpawn remains the game-state
    /// authority; the scene adapter adds only Godot collision/grounding clearance.
    /// </summary>
    public void ApplyRecoveredRac1Restart(RuntimeSpawn placement)
    {
        RuntimeSpawnScenePose pose = RuntimeSpawnSceneAdapter.ToScenePose(placement);
        GlobalPosition = pose.Position;
        Rotation = new Vector3(0f, pose.SceneYaw, 0f);
        ResetAfterPlacement(placement.Yaw);
    }

    internal Basis DevelopmentCameraBasis => _pitch.GlobalTransform.Basis;

    internal void ApplyDevelopmentReset(Vector3 spawnPosition)
    {
        GlobalPosition = spawnPosition;
        ResetAfterPlacement(_rac1Yaw.CurrentYaw);
    }

    internal void ApplyDevelopmentFlyMode(bool enabled)
    {
        Velocity = Vector3.Zero;
        _rac1Movement.Reset();
        _rac1JumpWasHeld = false;
        ResetAnimationState();
        if (enabled)
            RestoreHostCameraPresentation();
        else if (HasActiveRecoveredCamera)
            ApplyRecoveredCameraPresentation();
    }

    internal void RefreshDevelopmentCameraPresentation()
    {
        if (HasActiveRecoveredCamera)
            ApplyRecoveredCameraPresentation();
        else
            RestoreHostCameraPresentation();
    }

    private void ResetAfterPlacement(double nativeYaw)
    {
        Velocity = Vector3.Zero;
        _rac1Movement.Reset();
        _rac1WrenchMotion.Reset();
        _rac1Yaw.Reset(nativeYaw);
        _rac1DynamicSupport.Reset();
        ClearRac1HostSupportAnchor();
        _rac1CameraInitialized = false;
        _rac1RuntimeCameraState = null;
        _rac1JumpWasHeld = false;
        _rac1PresentationGrounded = false;
        UpdateRac1FacingPresentation();
        ResetAnimationState();
        _placed = false;
        _placeTries = 0;
    }

    private void StepRetailDerivedMovement(Vector2 move, bool jump, bool crouch)
    {
        bool grounded = IsOnFloor();
        bool jumpPressed = jump && !_rac1JumpWasHeld;
        _rac1JumpWasHeld = jump;
        double controlYaw = GetRac1ControlYaw();
        var (contact, groundNormal) = ProbeRac1Contact(
            grounded,
            IsOnCeiling());
        _rac1SurfaceActionIntent = UseRac1Gameplay
            ? Rac1SurfaceActionRouting.Select(contact)
            : null;
        if (UseRac1Gameplay && _rac1WrenchMotion.Active)
        {
            StepRac1WrenchMotion(contact, groundNormal);
            return;
        }

        var intent = new PlayerControlIntent(
            move.X,
            -move.Y,
            jump,
            jumpPressed,
            crouch,
            GetRac1PlanarBasis(),
            GetRac1NativePlanarBasis());
        Func<Rac1RatchetYawMode, double> resolveFacing =
            mode => _rac1Yaw.Step(move.X, -move.Y, controlYaw, mode).CurrentYaw;
        var step = UseRac1Gameplay
            ? _rac1Movement.Step(intent, contact, resolveFacing)
            : _rac1Movement.Step(intent, contact.MovementFacts, resolveFacing);
        _rac1PresentationGrounded = contact.MovementFacts.IsGrounded || _rac1Movement.IsOrdinaryEdgeFall;

        UpdateRac1FacingPresentation();

        var preContact = UseRac1Gameplay
            ? ResolveRac1PreContactStep(step, contact, groundNormal)
            : new Rac1OrdinaryGroundContactMotion.PreContactStep(
                step.PlanarX,
                step.PlanarY,
                step.Vertical);
        var resolvedDelta = contact.ApplySupportAndConveyor(
            new Rac1NativeVector3(
                preContact.PlanarX,
                preContact.Vertical,
                preContact.PlanarY));
        const float nativeTicksPerSecond = (float)Rac1RatchetMovementController.UpdateHz;
        Velocity = new Vector3(
            (float)resolvedDelta.X * nativeTicksPerSecond,
            (float)resolvedDelta.Y * nativeTicksPerSecond,
            (float)resolvedDelta.Z * nativeTicksPerSecond);
        if (Scripted && step.Vertical > 0d && !_scriptJumped)
        {
            _scriptJumped = true;
            GD.Print($"[PlayerHost] native R&C1 jump from {GlobalPosition}");
        }
    }

    private void StepRac1WrenchMotion(
        Rac1PlayerContactResult contact,
        Vector3? groundNormal)
    {
        var noOrdinaryInput = new PlayerControlIntent(
            0d,
            0d,
            false,
            false,
            false,
            GetRac1PlanarBasis(),
            GetRac1NativePlanarBasis());
        var contactStep = _rac1Movement.Step(noOrdinaryInput, contact);
        _rac1PresentationGrounded = contact.MovementFacts.IsGrounded || _rac1Movement.IsOrdinaryEdgeFall;
        Rac1WrenchDirection nativeLunge = _rac1WrenchMotion.Step();
        var hostLunge = GetRac1NativePlanarBasis().Transform(nativeLunge.X, nativeLunge.Y);
        var attackStep = new Rac1RatchetMovementController.StepResult(
            hostLunge.X,
            hostLunge.Y,
            contactStep.Vertical,
            contactStep.Phase,
            hostLunge.X != 0d || hostLunge.Y != 0d
                ? Rac1RatchetLocomotionState.Moving
                : contactStep.LocomotionState,
            contactStep.YawMode);

        var preContact = ResolveRac1PreContactStep(
            attackStep,
            contact,
            groundNormal);
        var resolvedDelta = contact.ApplySupportAndConveyor(
            new Rac1NativeVector3(
                preContact.PlanarX,
                preContact.Vertical,
                preContact.PlanarY));
        const float nativeTicksPerSecond = (float)Rac1RatchetMovementController.UpdateHz;
        Velocity = new Vector3(
            (float)resolvedDelta.X * nativeTicksPerSecond,
            (float)resolvedDelta.Y * nativeTicksPerSecond,
            (float)resolvedDelta.Z * nativeTicksPerSecond);
    }

    private Rac1OrdinaryGroundContactMotion.PreContactStep ResolveRac1PreContactStep(
        Rac1RatchetMovementController.StepResult step,
        Rac1PlayerContactResult contact,
        Vector3? groundNormal)
    {
        Vector3 admittedNormal = groundNormal ?? Vector3.Up;
        var preContact = Rac1OrdinaryGroundContactMotion.ResolvePreContactStep(
            step,
            contact.MovementFacts,
            admittedNormal.X,
            admittedNormal.Z,
            admittedNormal.Y);

        if (!contact.MovementFacts.IsGrounded ||
            step.Phase != Rac1RatchetMovementPhase.Grounded ||
            Math.Abs(step.PlanarX) + Math.Abs(step.PlanarY) <= 1e-12d ||
            !TryProbeRac1ProspectiveSupport(
                step.PlanarX,
                step.PlanarY,
                out double supportRise,
                out double supportAngle))
        {
            return preContact;
        }

        return preContact with
        {
            Vertical = Rac1OrdinaryGroundContactMotion.ResolveHostTransitionVertical(
                preContact.Vertical,
                supportRise,
                supportAngle),
        };
    }

    private bool TryProbeRac1ProspectiveSupport(
        double planarX,
        double planarY,
        out double supportRise,
        out double supportAngle)
    {
        supportRise = 0d;
        supportAngle = 0d;
        float envelope = (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportTransitionHostEnvelope;
        Vector3 target = GlobalPosition + new Vector3((float)planarX, 0f, (float)planarY);
        var query = PhysicsRayQueryParameters3D.Create(
            target + Vector3.Up * envelope,
            target + Vector3.Down * envelope);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 ||
            !hit.ContainsKey("collider") ||
            !hit.ContainsKey("position") ||
            !hit.ContainsKey("normal") ||
            !hit.ContainsKey("face_index"))
            return false;

        if (hit["collider"].As<Node>() is not RuntimeWorldScene.RuntimeCollisionBody3D collisionBody)
            return false;

        int faceIndex = (int)hit["face_index"];
        int? materialId = collisionBody.MaterialIdForFace(faceIndex);
        if (materialId is null || materialId < byte.MinValue || materialId > byte.MaxValue)
            return false;
        var candidateContact = Rac1PlayerContactResult.StaticWorld(
            true,
            rawFaceType: materialId.Value);
        if (Rac1SurfaceActionRouting.Select(candidateContact) is not null)
            return false;

        Vector3 normal = ((Vector3)hit["normal"]).Normalized();
        supportAngle = Math.Acos(Math.Clamp((double)normal.Dot(Vector3.Up), -1d, 1d));
        supportRise = ((Vector3)hit["position"]).Y - GlobalPosition.Y;
        return true;
    }

    private (Rac1PlayerContactResult Contact, Vector3? GroundNormal) ProbeRac1Contact(
        bool grounded,
        bool hitCeiling)
    {
        Vector3 origin = GlobalPosition;
        float supportMetric = (float)Rac1OrdinaryGroundContactMotion.OrdinarySupportContactMetricLimit;
        float probeUp = grounded ? 0.25f : supportMetric;
        float probeDown = grounded ? 2.0f : supportMetric;
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * probeUp,
            origin + Vector3.Down * probeDown);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || !hit.ContainsKey("collider"))
        {
            ClearRac1HostSupportAnchor();
            return (_rac1DynamicSupport.StepStatic(grounded, hitCeiling), null);
        }

        Vector3? groundNormal = hit.ContainsKey("normal")
            ? (Vector3)hit["normal"]
            : null;
        if (!grounded)
        {
            if (groundNormal is not { } candidateNormal ||
                !hit.ContainsKey("position"))
            {
                ClearRac1HostSupportAnchor();
                return (_rac1DynamicSupport.StepStatic(false, hitCeiling), null);
            }

            Vector3 candidatePosition = (Vector3)hit["position"];
            double correctionMetric = Math.Abs(origin.Y - candidatePosition.Y);
            double angle = Math.Acos(Math.Clamp((double)candidateNormal.Dot(Vector3.Up), -1d, 1d));
            if (!Rac1OrdinaryGroundContactMotion.AdmitsOrdinarySupport(correctionMetric, angle))
            {
                ClearRac1HostSupportAnchor();
                return (_rac1DynamicSupport.StepStatic(false, hitCeiling), null);
            }

            grounded = true;
        }
        var collider = hit["collider"].As<Node>();
        if (collider is RuntimeWorldScene.RuntimeCollisionBody3D collisionBody &&
            hit.ContainsKey("face_index"))
        {
            ClearRac1HostSupportAnchor();
            int faceIndex = (int)hit["face_index"];
            int? materialId = collisionBody.MaterialIdForFace(faceIndex);
            int? rawFaceType = materialId is >= byte.MinValue and <= byte.MaxValue
                ? materialId
                : null;
            return (
                _rac1DynamicSupport.StepStatic(
                    true,
                    hitCeiling,
                    rawFaceType),
                groundNormal);
        }

        if (UseRac1Gameplay &&
            RuntimeWorldScene.FindDynamicObjectRoot(collider) is
            { Source: { } dynamicOwner } dynamicRoot)
        {
            var contactKey = new Rac1MobyRuntimeKey(
                dynamicOwner.NativeClassId,
                dynamicOwner.InstanceIndex);
            if (!hit.ContainsKey("position"))
            {
                ClearRac1HostSupportAnchor();
                return (
                    _rac1DynamicSupport.Step(new Rac1DynamicSupportFacts(
                        IsGrounded: true,
                        HitCeiling: hitCeiling,
                        RawFaceType: null,
                        ContactedMoby: contactKey,
                        CurrentDynamicContact: contactKey,
                        PersistentSupportMoby: null,
                        SupportAnchor: new Rac1SupportAnchorState(0u, false),
                        SupportAnchorWorldPosition: null,
                        Conveyor: Rac1ConveyorTransfer.None)),
                    groundNormal);
            }

            Vector3 contactWorld = (Vector3)hit["position"];
            if (!_rac1HostSupportAnchorValid || _rac1HostSupportKey != contactKey)
            {
                _rac1HostSupportKey = contactKey;
                _rac1HostSupportLocalAnchor = dynamicRoot.ToLocal(contactWorld);
                _rac1HostSupportAnchorValid = true;
            }

            Vector3 supportAnchorWorld = dynamicRoot.ToGlobal(_rac1HostSupportLocalAnchor);
            Vec3 supportAnchorNative = ScenePlayerToNative(supportAnchorWorld);
            return (
                _rac1DynamicSupport.Step(new Rac1DynamicSupportFacts(
                    IsGrounded: true,
                    HitCeiling: hitCeiling,
                    RawFaceType: null,
                    ContactedMoby: contactKey,
                    CurrentDynamicContact: contactKey,
                    PersistentSupportMoby: contactKey,
                    SupportAnchor: new Rac1SupportAnchorState(1u, true),
                    SupportAnchorWorldPosition: new Rac1NativeVector3(
                        supportAnchorNative.X,
                        supportAnchorNative.Y,
                        supportAnchorNative.Z),
                    Conveyor: Rac1ConveyorTransfer.None)),
                groundNormal);
        }

        ClearRac1HostSupportAnchor();
        return (_rac1DynamicSupport.StepStatic(true, hitCeiling), groundNormal);
    }

    private void ClearRac1HostSupportAnchor()
    {
        _rac1HostSupportKey = null;
        _rac1HostSupportLocalAnchor = Vector3.Zero;
        _rac1HostSupportAnchorValid = false;
    }

    private void UpdateAnimationState(bool onFloor)
    {
        // Presentation follows the recovered controller state rather than the
        // retired DebugPlayer tuning. Jump anticipation is already native-air
        // presentation (sequence 7), while crouch states stay visually neutral
        // until a dedicated crouch presentation state is exposed.
        // Ordinary action state 2 keeps its locomotion selector through transient
        // support loss. The retained Veldin edge witness stays on sequence 4 for
        // all 16 unsupported ticks, so only actual jump/fall state may drive the
        // airborne presentation path.
        bool controllerAirborne =
            _rac1Movement.Phase != Rac1RatchetMovementPhase.Grounded &&
            !_rac1Movement.IsOrdinaryEdgeFall;
        bool recoveredSupportForPresentation = UseRac1Gameplay
            ? _rac1PresentationGrounded
            : onFloor;
        bool animationGrounded = recoveredSupportForPresentation && !controllerAirborne;
        bool justLanded = _animationGroundedInitialized && !_animationWasGrounded && animationGrounded;
        _animationGroundedInitialized = true;
        _animationWasGrounded = animationGrounded;

        bool attackRequested = _attackRequested;
        _attackRequested = false;
        float planarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
        if (_rac1Movement.LocomotionState is Rac1RatchetLocomotionState.Crouched or Rac1RatchetLocomotionState.CrouchTurning)
            planarSpeed = 0f;
        else if (_rac1Movement.LocomotionState == Rac1RatchetLocomotionState.Moving &&
                 _rac1Movement.YawMode == Rac1RatchetYawMode.GroundRun)
            planarSpeed = Math.Max(planarSpeed, (float)PlayerAnimationStateMachine.RunEnterSpeed);

        double verticalPresentation = _rac1Movement.Phase == Rac1RatchetMovementPhase.JumpAnticipation
            ? PlayerAnimationStateMachine.JumpRiseVelocity + 0.01d
            : Velocity.Y;

        PlayerAnimationState previous = AnimationState;
        PlayerAnimationState current = _animationStateMachine.Update(new PlayerAnimationFacts(
            animationGrounded, planarSpeed, verticalPresentation, justLanded, attackRequested));
        if (current != previous)
            GD.Print($"[PlayerHost] animation {previous} -> {current}");
    }
    private void RequestPrimaryAction()
    {
        if (UseRac1Gameplay && !Rac1GameplayAlive)
            return;

        if (UseRac1Gameplay)
        {
            // R&C1 presentation is admitted by the equipped weapon path. In
            // particular, a rejected item-10 request must not play wrench seq 23.
            Rac1PrimaryAttackRequested?.Invoke();
        }
        else
        {
            _attackRequested = true;
            CrateStrikeRequested?.Invoke();
        }
    }

    private void ResetAnimationState()
    {
        _animationStateMachine = new PlayerAnimationStateMachine();
        _animationGroundedInitialized = false;
        _animationWasGrounded = false;
        _attackRequested = false;
    }

    /// <summary>Measure each jump: air time, horizontal distance, apex height, net height change.</summary>
    private void TrackJump(bool onFloor)
    {
        if (!_airborne && !onFloor && Velocity.Y > 0.1f)
        {
            _airborne = true;
            _jumpStart = GlobalPosition;
            _jumpStartTime = _time;
            _jumpApexY = GlobalPosition.Y;
        }
        else if (_airborne)
        {
            _jumpApexY = Mathf.Max(_jumpApexY, GlobalPosition.Y);
            if (onFloor)
            {
                _airborne = false;
                var p = GlobalPosition;
                float horiz = new Vector2(p.X - _jumpStart.X, p.Z - _jumpStart.Z).Length();
                float apex = _jumpApexY - _jumpStart.Y;
                float net = p.Y - _jumpStart.Y;
                _lastJump = $"{_time - _jumpStartTime:0.00}s  {horiz:0.0}u  apex {apex:0.0}u  net {net:+0.0;-0.0;0}u";
                GD.Print($"[PlayerHost] jump landed: {_lastJump}");
            }
        }
    }

    /// <summary>
    /// Apply the right stick to OBP's existing third-person camera. This is host
    /// policy only and deliberately does not claim the current rates/deadzone as
    /// recovered R&amp;C1 camera behaviour.
    /// </summary>
    private void ApplyGamepadCamera(float delta, Vector2 rawIntent)
    {
        float magnitude = rawIntent.Length();
        if (magnitude <= GamepadCameraDeadzone)
            return;

        Vector2 intent = magnitude > 1f ? rawIntent / magnitude : rawIntent;
        _yaw.RotateY(-intent.X * GamepadCameraYawRadiansPerSecond * delta);
        float pitch = Mathf.Clamp(
            _pitch.Rotation.X - intent.Y * GamepadCameraPitchRadiansPerSecond * delta,
            Mathf.DegToRad(-82f),
            Mathf.DegToRad(55f));
        _pitch.Rotation = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>Pull the third-person camera in when the line from the pivot to it is blocked by geometry.</summary>
    private void UpdateCameraDistance()
    {
        var pivot = _pitch.GlobalPosition;
        var want = _pitch.GlobalTransform * new Vector3(0, 0, _cameraDistance);
        var query = PhysicsRayQueryParameters3D.Create(pivot, want);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        float dist = _cameraDistance;
        if (hit.Count > 0)
        {
            dist = Mathf.Clamp(pivot.DistanceTo((Vector3)hit["position"]) - 0.3f, 1.2f, _cameraDistance);
        }

        Camera.Position = Camera.Position.Lerp(new Vector3(0, 0, dist), 0.35f);
    }

    private void UpdateHud(bool onFloor)
    {
        var p = GlobalPosition;
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        string diagnostics = DevelopmentControls.DiagnosticsVisible ? BuildInputDiagnostics() : string.Empty;
        string developmentControls = DevelopmentControls.ControlSummary(UseRac1Gameplay);
        _hud.Text =
            $"pos {p.X:0.0} {p.Y:0.0} {p.Z:0.0}    speed {speed:0.0} u/s    {(DevelopmentControls.FlyEnabled ? "FLY" : onFloor ? "ground" : "air")}" +
            $"    anim {AnimationState}\n" +
            $"controller {MovementControllerLabel}    locomotion {_rac1Movement.LocomotionState}    yaw {_rac1Movement.YawMode}\n" +
            $"last jump: {_lastJump}\n" +
            diagnostics +
            $"WASD / left stick / Space + south face jump / C + right shoulder crouch / X + west face action\n" +
            developmentControls;
    }

    private string BuildInputDiagnostics()
    {
        var analogue = _rac1Movement.AnalogueInput;
        string pad = _liveInput.Diagnostic is { } diagnostic ? diagnostic.Format() : "none";
        string inputLine = FormattableString.Invariant(
            $"input host right/forward=({_liveInput.Move.X:0.000000},{-_liveInput.Move.Y:0.000000}) conditioned=({analogue.X:0.000000},{analogue.Y:0.000000}) mag={analogue.Magnitude:0.000000} uncapped={analogue.UncappedMagnitude:0.000000} band={analogue.SpeedBand}\n");
        string surfaceIntent = _rac1SurfaceActionIntent is { } intent
            ? $"0x{intent.NativeActionState:x2}/{intent.Interaction}"
            : "none";
        string movementLine = FormattableString.Invariant(
            $"native target step={_rac1Movement.TargetPlanarStep:0.00000000} actual step={Math.Sqrt((_rac1Movement.PlanarX * _rac1Movement.PlanarX) + (_rac1Movement.PlanarY * _rac1Movement.PlanarY)):0.00000000} locomotion={_rac1Movement.LocomotionState} yaw-mode={_rac1Movement.YawMode} surface-intent={surfaceIntent}\n");
        string yawLine = FormattableString.Invariant(
            $"yaw control={_rac1Yaw.ControlYaw:0.000000} target={_rac1Yaw.TargetYaw:0.000000} current={_rac1Yaw.CurrentYaw:0.000000} velocity={_rac1Yaw.YawVelocity:0.000000}\n");
        string cameraState = _rac1RuntimeCameraState is { } state
            ? FormattableString.Invariant(
                $" control={state.ControlHeadingRadians:0.000000} preferred={state.PreferredDistance:0.000} effective={state.EffectiveDistance:0.000}")
            : string.Empty;
        string cameraLine = FormattableString.Invariant(
            $"camera mode={CameraControllerLabel}{cameraState} manual=({_rac1Camera.ManualYawState:0.000000},{_rac1Camera.ManualPitchState:0.000000}) obstruction={_rac1Camera.ObstructionCorrection:0.000}/{_rac1Camera.ObstructionReleaseTicks} raw=({RawCameraIntent.X:0.000000},{RawCameraIntent.Y:0.000000})\n");
        return inputLine + movementLine + yawLine + cameraLine + $"gamepad {pad}\n";
    }

    /// <summary>
    /// Retune only the presentation camera for a real avatar while preserving the
    /// debug controller's collision and movement. This is an OBP camera choice,
    /// not a claim about the retail game's native camera constants.
    /// </summary>
    public void ConfigureAvatarPresentation(float avatarHeight)
    {
        if (!float.IsFinite(avatarHeight) || avatarHeight <= 0f)
        {
            return;
        }

        float height = Mathf.Clamp(avatarHeight, 0.8f, 3.5f);
        float clearance = Scripted ? 1.0f : 0.65f;
        float distanceScale = Scripted ? 5.5f : 4.5f;
        _cameraDistance = Mathf.Clamp(height * distanceScale, 4.5f, Scripted ? 10f : 8f);
        _pitch.Position = new Vector3(0f, height + clearance, 0f);
        if (!HasActiveRecoveredCamera)
            Camera.Position = new Vector3(0f, 0f, _cameraDistance);
    }
    /// <summary>Drop the capsule exactly onto the collision surface under the spawn point (deterministic). Returns true once placed.</summary>
    private bool SnapToGroundBelow()
    {
        var space = GetWorld3D().DirectSpaceState;
        float upwardReach = float.IsFinite(InitialGroundSnapUpwardReach)
            ? Math.Max(0f, InitialGroundSnapUpwardReach)
            : 8f;
        var from = GlobalPosition + Vector3.Up * upwardReach;
        var to = GlobalPosition + Vector3.Down * 800f;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0)
        {
            return false;
        }

        GlobalPosition = (Vector3)hit["position"] + Vector3.Up * 0.08f;
        Velocity = Vector3.Zero;
        GD.Print($"[PlayerHost] placed on collision at {GlobalPosition}  (normal {(Vector3)hit["normal"]})");
        return true;
    }

    /// <summary>Fixed timeline: drop + settle, walk forward, jump mid-stride, veer — so the capture shows motion + air time.</summary>
    private (Vector2 Move, bool Jump) ScriptedInput()
    {
        if (ScriptedStill)
        {
            return (Vector2.Zero, false);
        }

        if (UseRac1Gameplay)
        {
            // Veldin's authored start first settles from a higher collision
            // surface. Wait for that contact, then exercise the retail-backed
            // run -> ordinary primary attack -> maximum held jump -> fall path.
            float forward = _time is > 4.2 and < 7.2 ? -1f : 0f;
            if (!_scriptAttacked && _time > 4.7)
            {
                _scriptAttacked = true;
                Rac1PrimaryAttackRequested?.Invoke();
            }
            bool jump = _time is > 5.0 and < 5.3;
            return (new Vector2(0f, forward), jump);
        }

        // Legacy debug-controller capture used by the other games.
        float legacyForward = _time is > 0.7 and < 1.5 ? -1f : 0f;
        if (!_scriptAttacked && _time > 1.0)
        {
            _scriptAttacked = true;
            _attackRequested = true;
        }

        bool legacyJump = _time is > 1.7 and < 1.8;
        return (new Vector2(0f, legacyForward), legacyJump);
    }
}
