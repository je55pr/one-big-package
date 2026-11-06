using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// The eight shared orbit directions of class 806 (0x1e3ca0 / 0x1e3d20). The first live cluster builds them
/// from its basis and spins them once per tick (0x2d8908); every cluster reads the same rotating set.
/// </summary>
public sealed class Rac1NanotechOrbSpin
{
    private readonly Vector3[] _directions = new Vector3[Rac1NanotechCluster.OrbCount];
    private readonly Vector3[] _axes = new Vector3[Rac1NanotechCluster.OrbCount];
    private bool _ready;
    public IReadOnlyList<Vector3> Directions => _directions;
    public bool Ready => _ready;

    /// <summary>0x2d89f0 state 0 (master only): directions are the basis axes and their diagonals, each spun about a perpendicular axis.</summary>
    public void Initialize(float radius, Vector3 r0, Vector3 r1, Vector3 r2)
    {
        _directions[0] = Rac1NanotechCluster.SetLength(r0, radius); _axes[0] = r2;
        _directions[1] = Rac1NanotechCluster.SetLength(r1, radius); _axes[1] = r0;
        _directions[2] = Rac1NanotechCluster.SetLength(r2, radius); _axes[2] = r1;
        _directions[3] = Rac1NanotechCluster.SetLength(r0 + r2, radius); _axes[3] = Vector3.Cross(r1, _directions[3]);
        _directions[4] = Rac1NanotechCluster.SetLength(r0 + r1, radius); _axes[4] = Vector3.Cross(r2, _directions[4]);
        _directions[5] = Rac1NanotechCluster.SetLength(r1 - r2, radius); _axes[5] = Vector3.Cross(r0, _directions[5]);
        _directions[6] = Rac1NanotechCluster.SetLength(r0 - r2, radius); _axes[6] = Vector3.Cross(r1, _directions[6]);
        _directions[7] = Rac1NanotechCluster.SetLength(r0 - r1, radius); _axes[7] = Vector3.Cross(r2, _directions[7]);
        _ready = true;
    }

    public void Step(float spinStep, float radius)
    {
        if (!_ready) return;
        for (int i = 0; i < _directions.Length; i++)
            _directions[i] = Rac1NanotechCluster.SetLength(Rac1NanotechCluster.Rotate(_directions[i], _axes[i], spinStep), radius);
    }
}

/// <summary>
/// One class-806 Nanotech cloud's presentation state (update 0x2d89f0): it rides its crate, rests where the
/// crate stood, then launches eight orbs along curves to Ratchet. The first orb to arrive credits the single
/// health point (the host applies it through <see cref="Rac1NanotechCloudSession"/>); native trail glows and
/// glints then play on Ratchet. Positions are native Z-up. Draw geometry is produced by <see cref="Build"/>.
/// </summary>
public sealed class Rac1NanotechCluster
{
    public const int OrbCount = 8;
    public enum Phase { Riding = 1, Resting = 2, Flying = 3, Done = 4 }

    private sealed class Orb
    {
        public Rac1ParticleSession.Particle? Particle;
        /// <summary>In flight. Independent of <see cref="Particle"/>: a full particle pool must not stop an orb arriving.</summary>
        public bool Active;
        public float Distance, Breath;
        public Vector3 Start, Control, Position;
        public int Timer, Life;
    }

    /// <summary>What a tick needs from the host: Ratchet and the camera, in native space.</summary>
    public readonly record struct Frame(Vector3 Hero, Vector3 HeroTarget, Vector3 HeroUp, bool Visible);
    public readonly record struct Result(bool FirstArrival, bool Finished);

    private readonly Rac1NanotechOrbAssets _assets;
    private readonly Rac1NanotechOrbSpin _spin;
    private readonly Rac1ParticleSession _particles;
    private readonly Rac1PickupRandom _random;
    private readonly Orb[] _orbs = Enumerable.Range(0, OrbCount).Select(_ => new Orb()).ToArray();
    private readonly int[] _trailTimers = new int[4];
    private readonly float[] _trailFrames = new float[4];
    private Vector3 _spinAxis;
    private float _spinAngle, _bobPhase, _bob;
    private int _flightTimer;
    private bool _healed, _initialized;

    public Rac1NanotechCluster(Vector3 anchor, Rac1NanotechOrbAssets assets, Rac1NanotechOrbSpin spin,
        Rac1ParticleSession particles, Rac1PickupRandom random)
    {
        Position = anchor; _assets = assets; _spin = spin; _particles = particles; _random = random;
    }

    public Phase State { get; private set; } = Phase.Riding;
    /// <summary>The moby position: the crate while riding, where it stood once resting, Ratchet's side once healing.</summary>
    public Vector3 Position { get; private set; }
    public float Bob => _bob;
    public bool Healed => _healed;
    public int LiveOrbs => _orbs.Count(orb => orb.Particle is not null);
    /// <summary>Orbs still flying toward Ratchet (each credits nothing on its own; the first arrival heals).</summary>
    public int ActiveOrbs => _orbs.Count(orb => orb.Active);
    public IReadOnlyList<Vector3> OrbPositions => _orbs.Select(orb => orb.Position).ToArray();
    /// <summary>While riding, the moby follows its crate (0x2d89f0 state 1 copies the crate's position every tick).</summary>
    public void Follow(Vector3 cratePosition) { if (State == Phase.Riding) Position = cratePosition; }

    /// <summary>The crate broke: 0x2d89f0 state 2 (the cloud stays on the ground it fell to).</summary>
    public void CrateBroken() { if (State == Phase.Riding) State = Phase.Resting; }

    /// <summary>Launch toward Ratchet (0x2d89f0 state 3 entry): per-orb curves, flight lives 20..80 frames.</summary>
    public void Launch(Vector3 heroTarget, Vector3 heroUp)
    {
        if (State is Phase.Flying or Phase.Done) return;
        var k = _assets.K;
        EnsureInitialized(); // a cloud launched before its first tick still needs its orbit basis and spin
        // A launch needs eight orbs even if the cloud was never on screen (the native cloud always has been).
        var centre = Centre;
        for (int i = 0; i < OrbCount; i++)
        {
            var orb = _orbs[i];
            if (orb.Particle is not null && _particles.IsLive(orb.Particle)) { orb.Position = orb.Particle.Position; continue; }
            orb.Position = OrbPosition(i, centre);
            orb.Particle = _particles.Sparkle(orb.Position, 0, 1, k.OrbSize);
        }
        State = Phase.Flying; _flightTimer = k.FlightTimer;
        // 0x2d89f0 state 3: d = target - moby, d.z -= 0.5 (a native literal). The native target is the hero less
        // TargetOffset (.75) along his axis; the host supplies that point as heroTarget, so the constant is not reapplied.
        var toTarget = heroTarget - Position; toTarget.Z -= .5f;
        var side = -heroUp * k.CurveSide;
        // side x d, then d x that, leaves the component of "down" perpendicular to the flight line; turned -90 degrees about it.
        var perpendicular = Vector3.Cross(toTarget, Vector3.Cross(side, toTarget));
        side = SetLength(Rotate(perpendicular, toTarget, -MathF.PI / 2), k.CurveSide);
        for (int i = 0; i < OrbCount; i++)
        {
            var orb = _orbs[i];
            orb.Start = orb.Position;
            orb.Active = true;
            orb.Control = toTarget * .5f + Rotate(side, toTarget, i * 0.4488f);
            orb.Life = orb.Timer = _random.Inclusive(k.FlightLifeLow, k.FlightLifeHigh);
        }
    }

    private Vector3 Centre => Position + new Vector3(0, 0, .5f);

    private Vector3 OrbPosition(int index, Vector3 centre)
    {
        var d = Rotate(_spin.Directions[index], _spinAxis, _spinAngle);
        if (_orbs[index].Particle is not null && State == Phase.Resting) d = SetLength(d, _orbs[index].Distance);
        return centre + d + new Vector3(0, 0, _bob);
    }

    /// <summary>One native tick.</summary>
    public Result Step(in Frame frame)
    {
        var k = _assets.K;
        if (State == Phase.Done) return new(false, true);
        EnsureInitialized();
        _particles.Hero = frame.Hero;
        return State == Phase.Flying ? StepFlight(frame, k) : StepResting(frame, k);
    }

    /// <summary>0x2d89f0 state 0: random spin axis/angle and bob phase, trail timers and frames, the shared orbit basis.</summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        var k = _assets.K;
        float yaw = _random.Range(0, MathF.Tau), pitch = _random.Range(-MathF.PI / 2, MathF.PI / 2);
        _spinAxis = new(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
        _spinAngle = _random.Signed(0.7853981f, MathF.PI / 2);
        _bobPhase = _random.Range(0, MathF.PI);
        for (int i = 0; i < 4; i++) { _trailTimers[i] = k.TrailFrames; _trailFrames[i] = _random.Below(255); }
        for (int i = 0; i < OrbCount; i++) _orbs[i].Distance = k.OrbRadius;
        if (!_spin.Ready) _spin.Initialize(k.OrbRadius, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }

    private Result StepResting(in Frame frame, Rac1NanotechOrbAssets.Constants k)
    {
        _bob = k.BobAmplitude * MathF.Sin(_bobPhase);
        _bobPhase = AngleAdd(_bobPhase, k.BobStep);
        if (State == Phase.Resting)
            for (int i = 0; i < OrbCount; i++)
            {
                var orb = _orbs[i];
                if (orb.Particle is null) continue;
                orb.Distance = k.BreathAmplitude * MathF.Sin(orb.Breath) + k.OrbRadius;
                orb.Breath = AngleAdd(orb.Breath, _assets.PhaseSpeeds[i]);
            }
        int life = Vector2.Distance(new(Position.X, Position.Y), new(frame.Hero.X, frame.Hero.Y)) > 10 ? 5 : 40;
        var centre = Centre;
        for (int i = 0; i < OrbCount; i++)
        {
            var orb = _orbs[i];
            orb.Position = OrbPosition(i, centre);
            bool live = orb.Particle is not null && _particles.IsLive(orb.Particle);
            // Existing orb sprites follow their cluster even while it is hidden, so they never sit at stale positions.
            if (live)
            {
                orb.Particle!.Position = orb.Position;
                orb.Particle.Angle = (byte)(orb.Particle.Angle + k.OrbFrameStep);
            }
            // Only a cluster in view spends particles: sparkles and new orb sprites (the native draw gate).
            if (!frame.Visible) continue;
            _particles.Sparkle(orb.Position, life, 2, k.SparkleSize);
            if (!live) orb.Particle = _particles.Sparkle(orb.Position, 0, 1, k.OrbSize);
        }
        return new(false, false);
    }

    private Result StepFlight(in Frame frame, Rac1NanotechOrbAssets.Constants k)
    {
        bool first = false;
        if (_healed)
        {
            Position = frame.HeroTarget;
            if (Trail(frame, k) && _random.Below(5) == 0) Glints(frame, k);
        }
        if (--_flightTimer <= 0)
        {
            foreach (var orb in _orbs) Release(orb);
            State = Phase.Done;
            return new(false, true);
        }
        var target = frame.HeroTarget;
        for (int i = 0; i < OrbCount; i++)
        {
            var orb = _orbs[i];
            if (!orb.Active) continue;
            if (--orb.Timer <= 0)
            {
                // Arrival: the first orb of the cloud credits the single health point.
                if (!_healed) { _healed = true; first = true; Position = frame.HeroTarget; }
                Release(orb);
                continue;
            }
            float u = 1f - orb.Timer / (float)orb.Life;
            var previous = orb.Position;
            var b = orb.Start - orb.Control; var d = target - orb.Control;
            orb.Position = new(Cubic(b.X, orb.Start.X, target.X, d.X, u), Cubic(b.Y, orb.Start.Y, target.Y, d.Y, u), Cubic(b.Z, orb.Start.Z, target.Z, d.Z, u));
            if (orb.Particle is { } sprite && _particles.IsLive(sprite)) sprite.Position = orb.Position;
            Sparkle(orb.Position, k); Sparkle((orb.Position + previous) * .5f, k);
        }
        return new(first, false);
    }

    private void Sparkle(Vector3 at, Rac1NanotechOrbAssets.Constants k)
    {
        const float Jitter = 0.01f;
        var q = new Vector3(at.X + _random.Signed(0, Jitter), at.Y + _random.Signed(0, Jitter), at.Z + _random.Signed(0, Jitter));
        _particles.Sparkle(q, 20, 0, k.SparkleSize, k.SparkleColor);
    }

    private void Release(Orb orb)
    {
        if (orb.Particle is { } particle) _particles.Remove(particle);
        orb.Particle = null;
        orb.Active = false;
    }

    /// <summary>0x2d8510: four stacked glows on Ratchet that grow while they fade. Returns whether a timer still has over 48 frames.</summary>
    private bool Trail(in Frame frame, Rac1NanotechOrbAssets.Constants k)
    {
        bool busy = false;
        var offset = SetLength(frame.Hero - Position, -0.3f);
        var step = SetLength(offset, 0.1f);
        var at = Position + offset;
        float z0 = at.Z;
        for (int i = 0; i < 4; i++)
        {
            if (_trailTimers[i] > 0 && --_trailTimers[i] <= 0) { _trailTimers[i] = 0; at += step; continue; }
            if (_trailTimers[i] <= 0) { at += step; continue; }
            int left = _trailTimers[i];
            if (48 < left) busy = true;
            float f = _trailFrames[i] + k.TrailFrameSteps[i];
            if (f >= 255) f -= 255; else if (f <= 0) f += 255;
            _trailFrames[i] = f;
            float u = (255 - left) / 255f;
            uint color = Rac1ParticleSession.LerpColor(k.TrailColorA, k.TrailColorB, MathF.Abs(.5f - u));
            float fade = 1f - left / (float)k.TrailFrames;
            uint alpha = (uint)(int)((color >> 24) * (1f - fade));
            at.Z = z0 + k.TrailHeights[i];
            _particles.HeroGlow(at, alpha << 24 | color & 0xffffffu, (byte)((int)f & 255), false, 2, k.TrailSizes[i] * fade);
            _particles.HeroGlow(at, alpha << 24 | 0xffffffu, (byte)((int)f & 255), false, 2, k.TrailSizes[i] * fade * .7f);
            at += step;
        }
        return busy;
    }

    /// <summary>0x2d8418: two glints leaving sideways from the cloud.</summary>
    private void Glints(in Frame frame, Rac1NanotechOrbAssets.Constants k)
    {
        var d = frame.Hero - Position;
        float speed = _random.Range(k.GlintSpeedLow, k.GlintSpeedHigh) / 60f;
        var velocity = Rotate(Vector3.UnitZ * speed, d, _random.Angle());
        _particles.Glint(Position, velocity, k.GlintColorA, k.GlintLife, (byte)_random.Below(255), k.GlintSizeA);
        _particles.Glint(Position, velocity, k.GlintColorB, k.GlintLife, (byte)_random.Below(255), k.GlintSizeB);
    }

    /// <summary>Removes every live particle this cluster owns (world reset, crate loss).</summary>
    public void Dispose()
    {
        foreach (var orb in _orbs) Release(orb);
        State = Phase.Done;
    }

    /// <summary>The draw callback 0x2d9810 for the riding/resting cloud: the 290-point glow mesh and the ring halo.</summary>
    public IReadOnlyList<Rac1FxSurface> Build(Vector3 eye)
    {
        var surfaces = new List<Rac1FxSurface>();
        if (State is not (Phase.Riding or Phase.Resting)) return surfaces;
        var k = _assets.K;
        var centre = Centre + new Vector3(0, 0, _bob);
        var toEye = SetLength(eye - centre, 1);
        var side = SetLength(Vector3.Cross(Vector3.UnitZ, toEye), 1);
        if (side == Vector3.Zero) side = Vector3.UnitY;
        var up = Vector3.Cross(toEye, side);
        Vector3 ToWorld(Vector3 p) => centre + p.X * toEye + p.Y * side + p.Z * up;

        foreach (var strip in _assets.Strips)
            surfaces.Add(Rac1FxSurface.Strip(k.GlowTexture, Rac1FxBlend.Alpha, Array.ConvertAll(strip.Points, ToWorld), strip.Uv, strip.Colors));

        float scale = k.BobAmplitude == 0 ? 0 : _bob / k.BobAmplitude;
        uint pulse = (uint)(int)(scale * 64 + 128) << 24;
        uint inner = pulse | k.RingColorLow;
        Vector2[] ringUv = [new(.5f, .5f), new(.5f, .5f), new(.5f, 1f), new(.5f, 1f)];
        foreach (var quad in _assets.RingQuads)
        {
            var corners = new Vector3[4];
            for (int j = 0; j < 4; j++)
            {
                float size = j < 2 ? k.RingScale : k.RingScale * k.RingOuterScale;
                corners[j] = ToWorld(_assets.RingVertices[quad[j]] * size);
            }
            surfaces.Add(Rac1FxSurface.Quad(Rac1BeamSlot.EndpointTexture, Rac1FxBlend.Additive, corners, ringUv, [inner, inner, 0, 0]));
        }
        return surfaces;
    }

    // --- 0x257e20 cubic through p1..p2 and the rotation / vector helpers the native code uses ---------------------

    internal static float Cubic(float p0, float p1, float p2, float p3, float t)
    {
        float a = p0 - p1, b = p3 - p2, t2 = t * t, c = (p2 - p0) * t;
        b -= a; a -= b;
        return b * t2 * t + a * t2 + c + p1;
    }

    internal static Vector3 SetLength(Vector3 v, float length) => v.LengthSquared() > 1e-12f ? v * (length / v.Length()) : Vector3.Zero;

    internal static Vector3 Rotate(Vector3 v, Vector3 axis, float angle)
    {
        if (MathF.Abs(angle) < 1e-5f || axis.LengthSquared() < 1e-12f) return v;
        var k = Vector3.Normalize(axis);
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        return v * cos + Vector3.Cross(k, v) * sin + k * Vector3.Dot(k, v) * (1 - cos);
    }

    private static float AngleAdd(float a, float b)
    {
        float s = a + b;
        return s >= MathF.PI ? s - MathF.Tau : s < -MathF.PI ? s + MathF.Tau : s;
    }
}
