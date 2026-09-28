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

    /// <summary>Fixed timeline: drop + settle, walk forward, jump mid-stride, veer â€” so the capture shows motion + air time.</summary>
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
