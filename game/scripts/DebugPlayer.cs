using Godot;
using OBP.Godot.Controls;
using OBP.Godot.Player;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.Runtime.Player;

namespace OneBigPackage;

/// <summary>
/// Godot host for ordinary Ratchet play against reconstructed world collision.
/// Normal grounded/airborne movement always uses the retail-derived R&amp;C1
/// controller as OBP's explicit cross-game trilogy default. That reuse is an OBP
/// design choice, not evidence that GC or UYA used the same native controller.
///
/// Godot owns collision, floor/ceiling contacts, camera transforms and scene-unit
/// velocity. <c>F</c> fly/noclip and <c>R</c> manual respawn remain separate
/// development features. Headless capture can supply deterministic canned input.
/// </summary>
public partial class DebugPlayer : CharacterBody3D
{
    // --- host / development tuning ---------------------------------------------
    // Ordinary movement constants live in OBP.RAC1.Player. Only presentation
    // camera sensitivity and development-only fly speed remain host-tunable.
    public float MouseSensitivity { get; set; } = 0.0022f;
    public float FlySpeed { get; set; } = 45f;

    // OBP presentation policy only. These values are not recovered retail camera constants.
    public const float GamepadCameraYawRadiansPerSecond = 2.4f;
    public const float GamepadCameraPitchRadiansPerSecond = 2.0f;
    public const float GamepadCameraDeadzone = 0.12f;

    /// <summary>Third-person camera distance behind the capsule (pulled in when it would clip geometry).</summary>
    public const float CamDistance = 10f;

    /// <summary>Use the deterministic canned input instead of the real keyboard / mouse.</summary>
    public bool Scripted { get; set; }

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

    /// <summary>Development-only fly state, exposed for deterministic host smoke coverage.</summary>
    public bool DevelopmentFlyEnabled => _fly;

    /// <summary>Host spawn remembered by the development respawn seam.</summary>
    public Vector3 DebugSpawnPosition => _spawn;

    /// <summary>Current conditioned left-stick magnitude from the common R&amp;C1 controller.</summary>
    public double Rac1AnalogueMagnitude => _rac1Movement.AnalogueInput.Magnitude;

    /// <summary>Current native planar target step after analogue conditioning.</summary>
    public double Rac1TargetPlanarStep => _rac1Movement.TargetPlanarStep;

    /// <summary>Development-only request; the host resolves the aimed GC crate.</summary>
    public event Action? CrateStrikeRequested;

    /// <summary>Normal R&amp;C1 primary attack input; the RAC1 host resolves the equipped item.</summary>
    public event Action? Rac1PrimaryAttackRequested;

    /// <summary>Host-only keyboard selection seam for the bounded R&amp;C1 weapon inventory.</summary>
    public event Action<Rac1WeaponId>? Rac1WeaponSelectionRequested;

    /// <summary>Development host seam for respawning after the witnessed Veldin death/reset boundary.</summary>
    public event Action? Rac1RespawnRequested;

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
        if (UseRac1Gameplay && Rac1GameplayAlive)
            _attackRequested = true;
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

    /// <summary>Retail-backed RAC1 yaw recurrence mode for deterministic inspection.</summary>
    public Rac1RatchetYawMode Rac1YawMode => _rac1Movement.YawMode;

    /// <summary>Whether the RAC1 gameplay session currently admits player control.</summary>
    public bool Rac1GameplayAlive { get; set; } = true;

    /// <summary>Unconditioned right-stick camera intent; presentation scaling is intentionally unresolved.</summary>
    public Vector2 RawCameraIntent => _liveInput.CameraIntent;

    /// <summary>Current SDL/Godot controller diagnostic, when a joypad is connected.</summary>
    public RawGamepadDiagnostic? RawGamepadDiagnostic => _liveInput.Diagnostic;

    private Node3D _yaw = null!;
    private Node3D _pitch = null!;
    private Label _hud = null!;
    private float _cameraDistance = CamDistance;
    private double _time;
    private bool _landed;
    private bool _placed;
    private bool _scriptJumped;
    private bool _fly;
    private bool _inputDiagnosticsVisible;
    private int _placeTries;
    private Vector3 _spawn;
    private PlayerAnimationStateMachine _animationStateMachine = new();
    private bool _animationGroundedInitialized;
    private bool _animationWasGrounded;
    private bool _attackRequested;
    private bool _scriptAttacked;
    private readonly Rac1RatchetMovementController _rac1Movement = new();
    private readonly Rac1RatchetYawController _rac1Yaw = new();
    private readonly RawGamepadInput _rawInput = new();
    private RawPlayerInputFrame _liveInput;
    private bool _rac1JumpWasHeld;

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
        _spawn = GlobalPosition;

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

        FloorSnapLength = 1.5f;
        FloorMaxAngle = Mathf.DegToRad(60f);
        FloorStopOnSlope = true;
        MaxSlides = 6;
        SafeMargin = 0.1f;

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

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw.RotateY(-motion.Relative.X * MouseSensitivity);
            float p = Mathf.Clamp(
                _pitch.Rotation.X - motion.Relative.Y * MouseSensitivity,
                Mathf.DegToRad(-82f), Mathf.DegToRad(55f));
            _pitch.Rotation = new Vector3(p, 0, 0);
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Tab)
            {
                Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                    ? Input.MouseModeEnum.Visible
                    : Input.MouseModeEnum.Captured;
            }
            else if (key.Keycode == Key.F8)
            {
                _inputDiagnosticsVisible = !_inputDiagnosticsVisible;
            }
            else if (UseRac1Gameplay && key.Keycode == Key.Key1)
            {
                Rac1WeaponSelectionRequested?.Invoke(Rac1WeaponId.Wrench);
            }
            else if (UseRac1Gameplay && key.Keycode == Key.Key2)
            {
                Rac1WeaponSelectionRequested?.Invoke(Rac1WeaponId.FirstRanged);
            }
            else if (key.Keycode == Key.F)
            {
                _fly = !_fly;
                Velocity = Vector3.Zero;
                _rac1Movement.Reset();
                _rac1JumpWasHeld = false;
                ResetAnimationState();
                GD.Print($"[DebugPlayer] fly mode {(_fly ? "on" : "off")}");
            }
            else if (key.Keycode == Key.R)
            {
                if (UseRac1Gameplay && !Rac1GameplayAlive)
                    Rac1RespawnRequested?.Invoke();
                else
                    ResetToSpawn();
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

        if (!Scripted)
        {
            _liveInput = _rawInput.Read();
            ApplyGamepadCamera((float)delta, _liveInput.CameraIntent);
            if (_liveInput.ActionJustPressed)
                RequestPrimaryAction();
        }

        if (_fly)
        {
            _attackRequested = false;
            FlyStep((float)delta, _liveInput.Move);
            UpdateHud(true);
            return;
        }

        var (move, jump) = Scripted ? ScriptedInput() : (_liveInput.Move, _liveInput.JumpHeld);
        bool crouch = !Scripted && _liveInput.CrouchHeld;
        if (UseRac1Gameplay && !Rac1GameplayAlive)
        {
            move = Vector2.Zero;
            jump = false;
            crouch = false;
        }
        StepRetailDerivedMovement(move, jump, crouch);

        MoveAndSlide();
        bool isOnFloor = IsOnFloor();
        UpdateAnimationState(isOnFloor);

        if (!Scripted)
        {
            UpdateCameraDistance();
        }

        TrackJump(isOnFloor);

        if (!_landed && isOnFloor)
        {
            _landed = true;
            GD.Print($"[DebugPlayer] on the collision floor at {GlobalPosition} after {_time:0.00}s");
        }

        UpdateHud(isOnFloor);
    }

    private double GetRac1ControlYaw()
    {
        // The host owns camera presentation. R&C1 owns how raw planar stick
        // direction combines with this forward/control heading to form G+0x100.
        Vector3 sceneForward = _yaw.GlobalTransform.Basis * new Vector3(0f, 0f, -1f);
        sceneForward.Y = 0f;
        if (sceneForward.LengthSquared() <= 1e-8f) return _rac1Yaw.ControlYaw;
        sceneForward = sceneForward.Normalized();
        return Math.Atan2(sceneForward.Z, -sceneForward.X);
    }

    private PlayerPlanarBasis GetRac1PlanarBasis()
    {
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

    public void ResetToSpawn()
    {
        GlobalPosition = _spawn;
        Velocity = Vector3.Zero;
        _rac1Movement.Reset();
        _rac1Yaw.Reset(_rac1Yaw.CurrentYaw);
        _rac1JumpWasHeld = false;
        Rac1GameplayAlive = true;
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
        var step = _rac1Movement.Step(
            new PlayerControlIntent(
                move.X,
                -move.Y,
                jump,
                jumpPressed,
                crouch,
                GetRac1PlanarBasis(),
                GetRac1NativePlanarBasis()),
            new PlayerContactFacts(grounded, IsOnCeiling()),
            mode => _rac1Yaw.Step(move.X, -move.Y, controlYaw, mode).CurrentYaw);

        UpdateRac1FacingPresentation();

        const float nativeTicksPerSecond = (float)Rac1RatchetMovementController.UpdateHz;
        Velocity = new Vector3(
            (float)step.PlanarX * nativeTicksPerSecond,
            (float)step.Vertical * nativeTicksPerSecond,
            (float)step.PlanarY * nativeTicksPerSecond);
        if (Scripted && step.Vertical > 0d && !_scriptJumped)
        {
            _scriptJumped = true;
            GD.Print($"[DebugPlayer] native R&C1 jump from {GlobalPosition}");
        }
    }

    private void UpdateAnimationState(bool onFloor)
    {
        // Presentation follows the recovered controller state rather than the
        // retired DebugPlayer tuning. Jump anticipation is already native-air
        // presentation (sequence 7), while crouch states stay visually neutral
        // until a dedicated crouch presentation state is exposed.
        bool controllerAirborne = _rac1Movement.Phase != Rac1RatchetMovementPhase.Grounded;
        bool animationGrounded = onFloor && !controllerAirborne;
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
            GD.Print($"[DebugPlayer] animation {previous} -> {current}");
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
                GD.Print($"[DebugPlayer] jump landed: {_lastJump}");
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
        string diagnostics = _inputDiagnosticsVisible ? BuildInputDiagnostics() : string.Empty;
        _hud.Text =
            $"pos {p.X:0.0} {p.Y:0.0} {p.Z:0.0}    speed {speed:0.0} u/s    {(_fly ? "FLY" : onFloor ? "ground" : "air")}" +
            $"    anim {AnimationState}\n" +
            $"controller {MovementControllerLabel}    locomotion {_rac1Movement.LocomotionState}    yaw {_rac1Movement.YawMode}\n" +
            $"last jump: {_lastJump}\n" +
            diagnostics +
            $"WASD / left stick / Space + south face jump / C + right shoulder crouch / X + west face action\n" +
            $"mouse / right stick camera / F fly / R respawn / F8 input diagnostics / Tab cursor / Esc";
    }

    private string BuildInputDiagnostics()
    {
        var analogue = _rac1Movement.AnalogueInput;
        string pad = _liveInput.Diagnostic is { } diagnostic ? diagnostic.Format() : "none";
        string inputLine = FormattableString.Invariant(
            $"input raw right/forward=({_liveInput.Move.X:0.000000},{-_liveInput.Move.Y:0.000000}) conditioned=({analogue.X:0.000000},{analogue.Y:0.000000}) mag={analogue.Magnitude:0.000000} uncapped={analogue.UncappedMagnitude:0.000000} band={analogue.SpeedBand}\n");
        string movementLine = FormattableString.Invariant(
            $"native target step={_rac1Movement.TargetPlanarStep:0.00000000} actual step={Math.Sqrt((_rac1Movement.PlanarX * _rac1Movement.PlanarX) + (_rac1Movement.PlanarY * _rac1Movement.PlanarY)):0.00000000} locomotion={_rac1Movement.LocomotionState} yaw-mode={_rac1Movement.YawMode}\n");
        string yawLine = FormattableString.Invariant(
            $"yaw control={_rac1Yaw.ControlYaw:0.000000} target={_rac1Yaw.TargetYaw:0.000000} current={_rac1Yaw.CurrentYaw:0.000000} velocity={_rac1Yaw.YawVelocity:0.000000}\n");
        string cameraLine = FormattableString.Invariant(
            $"camera raw=({RawCameraIntent.X:0.000000},{RawCameraIntent.Y:0.000000}) host deadzone={GamepadCameraDeadzone:0.00} rates={GamepadCameraYawRadiansPerSecond:0.0}/{GamepadCameraPitchRadiansPerSecond:0.0} rad/s\n");
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
        Camera.Position = new Vector3(0f, 0f, _cameraDistance);
    }
    /// <summary>Drop the capsule exactly onto the collision surface under the spawn point (deterministic). Returns true once placed.</summary>
    private bool SnapToGroundBelow()
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = GlobalPosition + Vector3.Up * 8f;
        var to = GlobalPosition + Vector3.Down * 800f;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0)
        {
            return false;
        }

        GlobalPosition = (Vector3)hit["position"] + Vector3.Up * 0.08f;
        Velocity = Vector3.Zero;
        GD.Print($"[DebugPlayer] placed on collision at {GlobalPosition}  (normal {(Vector3)hit["normal"]})");
        return true;
    }

    /// <summary>Free 6-DOF camera-relative movement, no gravity or collision — for exploring an imported level.</summary>
    private void FlyStep(float delta, Vector2 move)
    {
        float lift = (Input.IsPhysicalKeyPressed(Key.Space) || Input.IsPhysicalKeyPressed(Key.E) ? 1f : 0f)
                     - (Input.IsPhysicalKeyPressed(Key.Ctrl) || Input.IsPhysicalKeyPressed(Key.Q) ? 1f : 0f);
        bool boost = Input.IsPhysicalKeyPressed(Key.Shift);

        Vector3 dir = _pitch.GlobalTransform.Basis * new Vector3(move.X, 0f, move.Y);
        dir += Vector3.Up * lift;
        if (dir.LengthSquared() > 1e-4f)
        {
            GlobalPosition += dir.Normalized() * (FlySpeed * (boost ? 3f : 1f) * delta);
        }

        Velocity = Vector3.Zero;
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
