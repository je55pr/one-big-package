using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// One slot of the original R&amp;C1 class-1471 beam effect manager (Veldin overlay
/// 0x2e2250 strand update, 0x2e2af0/0x2e2c88 strand draw, 0x2e3388 end quads).
/// The Veldin shooting robot (class 1440) claims one slot while it fires.
///
/// All positions are native Z-up. Rendering, camera and Godot conversion stay in
/// the host: <see cref="Build"/> only returns already interpreted quads whose
/// colours use the PS2 range (0x80 = 1.0, so a byte may reach about 2.0).
/// Original sparks (particle kind 0x45), optional lights and the
/// unrecovered global random call order are intentionally outside this type.
/// </summary>
public sealed class Rac1BeamSlot
{
    public const int PointCount = 20;
    /// <summary>The manager's authored pvars select Fx textures 0 (glow) and 1 (beam) plus 0x28.</summary>
    public const int GlowTexture = 0x28, BeamTexture = 0x29;
    /// <summary>Literal Fx texture used by the end quads (0x2e3388) and by the robot's glow.</summary>
    public const int EndpointTexture = 0xb;

    // Quads are returned as Rac1FxSurface triangle pairs (GS strip order 0,1,2,3).

    // 0x2e3700 constants: per-tick phase steps (radians), end quad sizes and strand shape.
    private const float AmplitudeStep = 0.17453292f, TwistStep = 0.80285144f, SwellStep = 0.05236f;
    private const float TwistPerPoint = 0.99483764f, SwellPerPoint = 0.20944f;
    private const float ScrollPerTick = 0.2f, ScrollWrap = 8f;
    private const float HalfWidth = 0.6f, TipHalfWidth = 0.05f;

    private readonly Vector3[][] _strands = [new Vector3[PointCount], new Vector3[PointCount]];
    private float _amplitudePhase, _twistPhase, _swellPhase, _scroll;
    private bool _started;

    public Vector3 Origin { get; private set; }
    public Vector3 Endpoint { get; private set; }
    /// <summary>Strand-0 point 18 in world space (0x1e6e10): where the second end quad sits.</summary>
    public Vector3 Tip { get; private set; }
    public float Scroll => _scroll;
    public IReadOnlyList<Vector3[]> Strands => _strands;

    /// <summary>One native tick (the manager moby's update, 0x2e1df0 -> 0x2e2250) with the owner's current muzzle and endpoint.</summary>
    public void Step(Vector3 origin, Vector3 endpoint, Rac1PickupRandom random)
    {
        Origin = origin; Endpoint = endpoint;
        float length = (endpoint - origin).Length();
        float step = length / PointCount;
        if (!_started)
        {
            _started = true;
            for (int strand = 0; strand < 2; strand++)
                for (int i = 0; i < PointCount; i++) _strands[strand][i] = new(step * i, 0, 0);
        }
        _amplitudePhase = AngleAdd(_amplitudePhase, AmplitudeStep);
        _twistPhase = AngleSub(_twistPhase, TwistStep);
        _swellPhase = AngleAdd(_swellPhase, SwellStep);
        float phaseStep = 360f / (PointCount * .5f) * 0.017453292f;
        for (int j = 0; j < PointCount - 1; j++)
        {
            float amplitude = j / (float)PointCount * .3f + .1f;
            amplitude *= MathF.Sin(Wrap(_amplitudePhase + j * phaseStep));
            _strands[0][j] = new(step * j, 0, amplitude);
        }
        for (int k = 0; k < PointCount - 1; k++)
        {
            float twist = Wrap(_twistPhase + k * TwistPerPoint), swell = Wrap(_swellPhase + k * SwellPerPoint);
            float z = MathF.Sin(swell) * .5f * MathF.Sin(twist) + random.Signed(0, .2f);
            _strands[1][k] = new(step * k, MathF.Cos(twist) * .3f, z);
        }
        Tip = ToWorld(Frame(origin, endpoint), _strands[0][PointCount - 2]);
        // The draw callback scrolls its texture once per frame while the slot is live.
        _scroll -= ScrollPerTick;
        if (_scroll <= -ScrollWrap) _scroll += ScrollWrap;
    }

    /// <summary>
    /// Per-tick end sparks of 0x2e2250, called after <see cref="Step"/>: at the muzzle a spark drifting 20-40 degrees
    /// off the beam axis and, one tick in three, a 4-frame 120000 flare .3 ahead; at the tip a spark drifting in a
    /// random direction and, one tick in three, a flare. (Native turns the muzzle sparks by the joint matrix, which
    /// is not baked; the beam frame stands in.) The robot claims its slot with spark tag mode 0, so every one drifts.
    /// </summary>
    public void EmitSparks(Rac1ParticleSession particles, Rac1PickupRandom random)
    {
        var frame = Frame(Origin, Endpoint);
        float speed = random.Range(.01f, .05f), angle = random.Signed(0.34906584f, 0.6981317f);
        particles.Spark(Origin, MathF.Cos(angle) * speed * frame.Along + MathF.Sin(angle) * speed * frame.Side);
        if (random.Below(3) == 0) particles.Spark(Origin + frame.Along * .3f, Vector3.Zero, 120000, 4);
        random.Range(.01f, .05f); // the native call order draws and discards a speed here
        particles.Spark(Tip, RandomDirection(random, .01f, .05f));
        if (random.Below(3) == 0) particles.Spark(Tip, Vector3.Zero, 120000, 4);
    }

    /// <summary>A vector of random length in [min, max] in a random direction (two random angles, 0x257d00).</summary>
    public static Vector3 RandomDirection(Rac1PickupRandom random, float minimum, float maximum)
    {
        float length = random.Range(minimum, maximum), a = random.Angle(), b = random.Angle();
        return new(MathF.Cos(a) * MathF.Cos(b) * length, MathF.Sin(a) * MathF.Cos(b) * length, MathF.Sin(b) * length);
    }

    /// <summary>The slot's quads in native draw order for a camera at <paramref name="eye"/>: end quads, then each strand with its glow.</summary>
    public IReadOnlyList<Rac1FxSurface> Build(Vector3 eye, Rac1PickupRandom random)
    {
        var quads = new List<Rac1FxSurface>();
        float muzzleScale = random.Range(.3f, .325f);
        var jitter = new Vector3(random.Signed(0, .05f), random.Signed(0, .05f), random.Signed(0, .05f));
        AddEndpoint(quads, Origin, eye, muzzleScale, jitter);
        AddEndpoint(quads, Tip, eye, random.Range(.2f, .225f), jitter);
        var frame = Frame(Origin, Endpoint);
        AddStrand(quads, _strands[0], frame, eye, BeamAlpha: 0x80, GlowAlpha: 0x40);
        AddStrand(quads, _strands[1], frame, eye, BeamAlpha: 0x30, GlowAlpha: 0x10);
        return quads;
    }

    private void AddStrand(List<Rac1FxSurface> quads, Vector3[] points, Basis frame, Vector3 eye, uint BeamAlpha, uint GlowAlpha)
    {
        uint beam = BeamAlpha << 24 | 0x7F7F7F, glow = GlowAlpha << 24 | 0x7F4040;
        float half = HalfWidth, glowHalf = half * 2, s = _scroll;
        var world = points.Select(p => ToWorld(frame, p)).ToArray();
        (Vector3 A, Vector3 B) Pair(int index, float width)
        {
            var from = world[index]; var direction = Unit(world[index + 1] - from, Vector3.UnitX);
            var side = Unit(Vector3.Cross(eye - from, direction), Vector3.UnitZ);
            return (from + side * width, from - side * width);
        }
        var previous = Pair(0, half); var previousGlow = Pair(0, glowHalf);
        for (int i = 1; i < PointCount - 1; i++)
        {
            var next = Pair(i, half); var nextGlow = Pair(i, glowHalf);
            // The first segment fades in from the muzzle: its two root vertices carry alpha 0.
            uint rootBeam = i == 1 ? 0xFFFFFFu : beam, rootGlow = i == 1 ? 0x7F4040u : glow;
            quads.Add(Rac1FxSurface.Quad(BeamTexture, Rac1FxBlend.Additive, [previous.A, previous.B, next.A, next.B],
                [new(s, 0), new(s, 1), new(s + 1, 0), new(s + 1, 1)], [rootBeam, rootBeam, beam, beam]));
            quads.Add(Rac1FxSurface.Quad(GlowTexture, Rac1FxBlend.Additive, [previousGlow.A, previousGlow.B, nextGlow.A, nextGlow.B],
                [new(1, 0), new(1, 1), new(0, 0), new(0, 1)], [rootGlow, rootGlow, glow, glow]));
            previous = next; previousGlow = nextGlow;
            // Hermite ease over i/20 (interpreted 0 -> 1, matching width = half + (.05 - half) * taper): .6 at the muzzle, .05 at the tip.
            float t = i / (float)PointCount, taper = 3 * t * t - 2 * t * t * t;
            half = HalfWidth + (TipHalfWidth - HalfWidth) * taper;
            glowHalf = half * 2;
        }
    }

    /// <summary>0x2e3388: a jittered opaque-white core (alpha blend) and a 3x blue-violet additive halo, both Fx texture 0xb.</summary>
    private static void AddEndpoint(List<Rac1FxSurface> quads, Vector3 position, Vector3 eye, float scale, Vector3 jitter)
    {
        var (side, up) = CameraAxes(position, eye);
        Vector3[] Corners(float size, Vector3 offset) =>
        [
            position + offset + (-side + up) * size, position + offset + (-side - up) * size,
            position + offset + (side + up) * size, position + offset + (side - up) * size,
        ];
        Vector2[] uv = [new(0, 0), new(0, 1), new(1, 0), new(1, 1)];
        quads.Add(Rac1FxSurface.Quad(EndpointTexture, Rac1FxBlend.Alpha, Corners(scale, jitter), uv, [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]));
        quads.Add(Rac1FxSurface.Quad(EndpointTexture, Rac1FxBlend.Additive, Corners(scale * 3, default), uv, [0x607F4040u, 0x607F4040u, 0x607F4040u, 0x607F4040u]));
    }

    /// <summary>Camera-facing side/up axes about a point, using the world up axis (0,0,1) as the native code does.</summary>
    public static (Vector3 Side, Vector3 Up) CameraAxes(Vector3 position, Vector3 eye)
    {
        var toEye = Unit(eye - position, Vector3.UnitX);
        var side = -Unit(Vector3.Cross(toEye, Vector3.UnitZ), Vector3.UnitY);
        return (side, Vector3.Cross(side, toEye));
    }

    /// <summary>The robot's always-drawn glow billboard (0x2e1c78): a camera-facing additive quad of half-size .2 at chain point 1 lowered by .333.</summary>
    public static Rac1FxSurface RobotGlow(Vector3 socketOne, Vector3 eye)
    {
        var position = socketOne - Vector3.UnitZ * 0.333f;
        var (side, up) = CameraAxes(position, eye);
        const float size = .2f;
        return Rac1FxSurface.Quad(EndpointTexture, Rac1FxBlend.Additive,
            [position + (-side + up) * size, position + (-side - up) * size, position + (side + up) * size, position + (side - up) * size],
            [new(0, 0), new(0, 1), new(1, 0), new(1, 1)], [0x80FFFFFFu, 0x80FFFFFFu, 0x80FFFFFFu, 0x80FFFFFFu]);
    }

    private readonly record struct Basis(Vector3 Along, Vector3 Side, Vector3 Up, Vector3 Origin);

    private static Basis Frame(Vector3 origin, Vector3 endpoint)
    {
        var along = Unit(endpoint - origin, Vector3.UnitX);
        var side = -Unit(Vector3.Cross(along, Vector3.UnitZ), Vector3.UnitY);
        return new(along, side, Vector3.Cross(side, along), origin);
    }

    private static Vector3 ToWorld(Basis frame, Vector3 local) =>
        local.X * frame.Along + local.Y * frame.Side + local.Z * frame.Up + frame.Origin;

    private static Vector3 Unit(Vector3 value, Vector3 fallback) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;

    private static float Wrap(float angle) => angle >= MathF.PI ? angle - MathF.Tau : angle < -MathF.PI ? angle + MathF.Tau : angle;
    private static float AngleAdd(float angle, float delta) => Wrap(angle + delta);
    private static float AngleSub(float angle, float delta) => Wrap(angle - delta);
}
