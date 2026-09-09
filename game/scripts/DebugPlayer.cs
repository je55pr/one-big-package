using Godot;

namespace OneBigPackage;

/// <summary>
/// Checkpoint H: the debug capsule. A plain <see cref="CharacterBody3D"/> with
/// gravity, WASD + mouse-look, jump, an <c>F</c> fly / noclip toggle and an
/// <c>R</c> respawn, moving against the decoded octree collision
/// (<see cref="OBP.Godot.RuntimeWorldScene"/>'s <see cref="StaticBody3D"/> bodies).
/// No Ratchet movement behaviour is reconstructed here — this is a stand-in so
/// the reconstructed collision can be walked on, plus a HUD reporting position,
/// speed and the last jump's air time / distance / apex for movement tuning.
///
/// In headless capture (<see cref="Scripted"/>) it runs a fixed canned input so
/// the screenshot deterministically shows it having walked and jumped.
/// </summary>
public partial class DebugPlayer : CharacterBody3D
{
    // --- movement tuning -------------------------------------------------------
    // Settled baseline for a bare debug controller (no double-jump / ledge grab),
    // calibrated against two Oozla references: the debris-jump line by Ratchet's
    // ship — a running jump just clears one gap — and the Dynamo ledge — a single
    // jump must NOT clear it. Gives ~6.7 u reach, ~2.6 u apex (~1 body-height),
    // 0.67 s air. Gravity + JumpVelocity set the arc; MoveSpeed sets the distance.
    public float MoveSpeed { get; set; } = 10f;
    public float JumpVelocity { get; set; } = 17f;
    public float Gravity { get; set; } = 52f;
    public float TerminalVelocity { get; set; } = 60f; // just a tunnel guard; real fall speed stays well under this
    public float MouseSensitivity { get; set; } = 0.0022f;
    public float FlySpeed { get; set; } = 45f;

    /// <summary>Third-person camera distance behind the capsule (pulled in when it would clip geometry).</summary>
    public const float CamDistance = 10f;

    /// <summary>Use the deterministic canned input instead of the real keyboard / mouse.</summary>
    public bool Scripted { get; set; }

    /// <summary>Keep scripted captures stationary after ground placement.</summary>
    public bool ScriptedStill { get; set; }

    /// <summary>Development-only request; the host resolves the aimed GC crate.</summary>
    public event Action? CrateStrikeRequested;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Presentation-only root; replacing its visual never changes controller physics.</summary>
    public PlayerVisualRoot VisualRoot { get; private set; } = null!;

    private Node3D _yaw = null!;
    private Node3D _pitch = null!;
    private Label _hud = null!;
    private double _time;
    private bool _landed;
    private bool _placed;
    private bool _scriptJumped;
    private bool _fly;
    private int _placeTries;
    private Vector3 _spawn;

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
            else if (key.Keycode == Key.X)
            {
                CrateStrikeRequested?.Invoke();
            }
            else if (key.Keycode == Key.F)
            {
                _fly = !_fly;
                Velocity = Vector3.Zero;
                GD.Print($"[DebugPlayer] fly mode {(_fly ? "on" : "off")}");
            }
            else if (key.Keycode == Key.R)
            {
                GlobalPosition = _spawn;
                Velocity = Vector3.Zero;
                _placed = false;
                _placeTries = 0;
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

        if (_fly)
        {
            FlyStep((float)delta);
            UpdateHud(true);
            return;
        }

        Vector3 velocity = Velocity;
        bool onFloor = IsOnFloor();
        if (onFloor && velocity.Y < 0f)
        {
            velocity.Y = 0f;
        }
        else
        {
            velocity.Y = Mathf.Max(velocity.Y - Gravity * (float)delta, -TerminalVelocity);
        }

        var (move, jump) = Scripted ? ScriptedInput() : LiveInput();

        Vector3 wish = _yaw.GlobalTransform.Basis * new Vector3(move.X, 0f, move.Y);
        wish.Y = 0f;
        wish = wish.LengthSquared() > 1e-4f ? wish.Normalized() : Vector3.Zero;

        velocity.X = wish.X * MoveSpeed;
        velocity.Z = wish.Z * MoveSpeed;

        if (jump && onFloor)
        {
            velocity.Y = JumpVelocity;
            if (Scripted && !_scriptJumped)
            {
                _scriptJumped = true;
                GD.Print($"[DebugPlayer] jump from {GlobalPosition}");
            }
        }

        Velocity = velocity;
        MoveAndSlide();

        if (!Scripted)
        {
            UpdateCameraDistance();
        }

        TrackJump(IsOnFloor());

        if (!_landed && IsOnFloor())
        {
            _landed = true;
            GD.Print($"[DebugPlayer] on the collision floor at {GlobalPosition} after {_time:0.00}s");
        }

        UpdateHud(IsOnFloor());
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

    /// <summary>Pull the third-person camera in when the line from the pivot to it is blocked by geometry.</summary>
    private void UpdateCameraDistance()
    {
        var pivot = _pitch.GlobalPosition;
        var want = _pitch.GlobalTransform * new Vector3(0, 0, CamDistance);
        var query = PhysicsRayQueryParameters3D.Create(pivot, want);
        query.Exclude = new global::Godot.Collections.Array<Rid> { GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

        float dist = CamDistance;
        if (hit.Count > 0)
        {
            dist = Mathf.Clamp(pivot.DistanceTo((Vector3)hit["position"]) - 0.3f, 1.2f, CamDistance);
        }

        Camera.Position = Camera.Position.Lerp(new Vector3(0, 0, dist), 0.35f);
    }

    private void UpdateHud(bool onFloor)
    {
        var p = GlobalPosition;
        float speed = new Vector2(Velocity.X, Velocity.Z).Length();
        _hud.Text =
            $"pos {p.X:0.0} {p.Y:0.0} {p.Z:0.0}    speed {speed:0.0} u/s    {(_fly ? "FLY" : onFloor ? "ground" : "air")}\n" +
            $"last jump: {_lastJump}\n" +
            $"MoveSpeed {MoveSpeed:0.#}  JumpVelocity {JumpVelocity:0.#}  Gravity {Gravity:0.#}  (WASD / Space / X crate strike / F fly / R respawn / Tab cursor / Esc)";
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
    private void FlyStep(float delta)
    {
        var (move, _) = LiveInput();
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

    private (Vector2 Move, bool Jump) LiveInput()
    {
        var move = new Vector2(
            (Input.IsPhysicalKeyPressed(Key.D) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.A) ? 1f : 0f),
            (Input.IsPhysicalKeyPressed(Key.S) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.W) ? 1f : 0f));
        return (move, Input.IsPhysicalKeyPressed(Key.Space));
    }

    /// <summary>Fixed timeline: drop + settle, walk forward, jump mid-stride, veer — so the capture shows motion + air time.</summary>
    private (Vector2 Move, bool Jump) ScriptedInput()
    {
        if (ScriptedStill)
        {
            return (Vector2.Zero, false);
        }

        // A short step in + a jump (for the telemetry) — kept brief so an
        // edge-of-platform ship spawn doesn't walk the capsule off into a
        // crevasse before the capture frame.
        float forward = _time is > 0.7 and < 1.5 ? -1f : 0f;
        bool jump = _time is > 1.7 and < 1.8;
        return (new Vector2(0f, forward), jump);
    }
}
