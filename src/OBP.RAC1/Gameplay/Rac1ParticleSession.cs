using System.Numerics;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Fixed-update R&C1 particle records. Constructors and recurrences are the
/// ordinary NTSC paths at 0x26b230/26b500 (flame), 0x269958/269a70 (trail),
/// and 0x26a9f0/26ac78 (splitting explosion/smoke). Rendering and world queries
/// are external. Host seeds do not reproduce the game's global random ordering.
/// </summary>
public sealed partial class Rac1ParticleSession(Rac1EffectAssets assets, Rac1PickupRandom random, int capacity = 2048)
{
    public const float NativeSizeUnits = 210000;
    public const float Dt = 1f / 60;
    public enum Blend : byte { Alpha = 0x44, Additive = 0x48 }

    public sealed class Particle
    {
        public int Slot { get; internal init; }
        public long Identity { get; internal init; }
        public int Type { get; internal init; }
        public int Texture { get; internal set; }
        public Blend Blending { get; internal init; }
        public byte DepthRange { get; internal init; }
        public Vector3 Position { get; internal set; }
        public Vector3 Velocity { get; internal set; }
        public float Size => RawSize / NativeSizeUnits;
        public uint Color { get; internal set; }
        public byte Angle { get; internal set; }
        public float Rotation => Angle * (MathF.Tau / 256);
        public int Remaining { get; internal set; }
        public int Total { get; internal set; }
        public bool Smoke => (Flags & 1) != 0 && Type == 11;
        public int Generation { get; internal set; }
        internal float RawSize, BaseSize, Speed, Reach;
        internal uint StartColor, EndColor;
        internal byte Flags, Spin;
        internal int AuxiliaryLife;
        internal Vector3 EndVelocity;
        internal int Stage, MiddleLife, FinalLife;
        internal float EndSize;
    }

    private readonly Particle?[] _slots = capacity is > 0 and <= 65536 ? new Particle[capacity] :
        throw new ArgumentOutOfRangeException(nameof(capacity));
    private int _highest = -1;
    private long _nextIdentity;
    private int _tick;
    public int Count { get; private set; }
    public IEnumerable<Particle> Live
    {
        get { for (int i = 0; i <= _highest; i++) if (_slots[i] is { } p) yield return p; }
    }
    public bool IsLive(Particle particle) => ReferenceEquals(_slots[particle.Slot], particle);

    private Particle? Allocate(int type, int textureType, int textureChoice, Blend blend, Vector3 position)
    {
        int free = Array.FindIndex(_slots, p => p is null);
        if (free < 0) return null;
        if (!assets.ParticleChoices.TryGetValue(textureType, out var choices) || (uint)textureChoice >= choices.Length)
            throw new InvalidDataException("Particle constructor requested an absent native texture choice.");
        var p = new Particle
        {
            Slot = free,
            Identity = ++_nextIdentity,
            Type = type,
            Texture = choices[textureChoice],
            Position = position,
            Blending = blend,
            Angle = type switch
            {
                2 => (byte)(int)random.Range(0, 255),
                8 => 0,
                12 => (byte)random.Below(255),
                _ => (byte)random.Below(256)
            },
            DepthRange = type is 2 or 12 or 16 ? (byte)0x44 : (byte)0xa4
        };
        _slots[free] = p; _highest = Math.Max(_highest, free); Count++;
        return p;
    }

    public Particle? Flame(Vector3 position, Vector3 velocity, float reach, bool longLook)
    {
        var p = Allocate(12, longLook ? 24 : 12, longLook ? 0 : random.Below(8),
            longLook ? Blend.Additive : Blend.Alpha, position);
        if (p is null) return null;
        p.Flags = (byte)(4 | (longLook ? 1 : 0));
        p.Velocity = velocity; p.Reach = reach;
        p.Remaining = p.Total = (int)random.Range(longLook ? 25 : 20, longLook ? 35 : 30);
        p.RawSize = (longLook ? 1 : .25f) * NativeSizeUnits;
        p.StartColor = LerpColor(longLook ? 0x08002040u : 0x20202080u,
            longLook ? 0x08004040u : 0x20208080u, random.Range(0, 1));
        p.EndColor = LerpColor(longLook ? 0x10004040u : 0x1820c0c0u,
            longLook ? 0x10002040u : 0x182080c0u, random.Range(0, 1));
        p.Color = longLook ? 0x08002040u : 0x20202080u;
        p.Spin = unchecked((byte)(int)random.Signed(1, 4));
        return p;
    }

    public IReadOnlyList<Particle> Pyrocitor(Vector3 origin, Vector3 forward, float unobstructedReach)
    {
        float reach = Math.Clamp(unobstructedReach, 0, 12);
        var created = new List<Particle>(4);
        float speed = Math.Min(MathF.Sqrt(2 * 48 * Dt * Dt * reach), 16 * Dt);
        Vector3 direction = SetLength(forward, speed);
        int longCount = reach < 4 ? 1 : 2;
        float spread = (3 + (1 - 3) * (reach / 12)) * Dt;
        for (int i = 0; i < 2 + longCount; i++)
        {
            Vector3 velocity = Sphere(0, spread) + direction;
            Vector3 position = origin + direction * random.Range(0, 1);
            if (Flame(position, velocity, reach, i < longCount) is { } p) created.Add(p);
        }
        return created;
    }

    public Particle? Trail(Vector3 position)
    {
        uint[] colors = [0x2fffffffu, 0x2f00ffffu, 0x2f007fffu, 0x2f004fffu];
        var p = Allocate(4, 4, 0, Blend.Additive, position);
        if (p is null) return null;
        p.Velocity = SetLength(new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1)), Dt);
        p.StartColor = p.Color = colors[random.Below(colors.Length)];
        p.EndColor = 0x00004fff;
        p.Remaining = p.Total = 45;
        return p;
    }

    public Particle? ExplosionSpark(Vector3 position, Vector3 push, uint startColor, uint endColor,
        int lifetime, int smokeDelay, int generation, byte flags, float rawSize, float speed)
    {
        if (lifetime <= 0) return null;
        var p = Allocate(11, 11, 0, random.Below(100) < 20 ? Blend.Alpha : Blend.Additive, position);
        if (p is null) return null;
        p.Velocity = push + SetLength(new(random.Range(-1, 1), random.Range(-1, 1), random.Range(-1, 1)), speed);
        p.Speed = speed * .75f;
        p.StartColor = p.Color = startColor; p.EndColor = endColor;
        p.Remaining = p.Total = lifetime; p.AuxiliaryLife = smokeDelay;
        p.Generation = generation; p.Flags = flags; p.BaseSize = rawSize;
        return p;
    }

    public void Bomb(Vector3 origin, Vector3 camera, Vector3 reflectedPush)
    {
        uint[] start = [0x4f008fffu, 0x4f008fffu, 0x4f007fffu, 0x4f006fffu, 0x2fffffffu, 0x2fffffffu];
        uint[] end = [0x2f005f7fu, 0x2f004f7fu, 0x2f003f7fu, 0x2f00004fu, 0x2f000000u, 0x3f000000u];
        float distance = Vector3.Distance(origin, camera);
        int count = distance < 6 ? (int)distance + 1 : 4;
        for (int i = 0; i < count; i++)
        {
            float speed = random.Range(8, 10) * Dt - Math.Max(0, 7 - distance) * Dt;
            ExplosionSpark(origin, reflectedPush, start[random.Below(6)], end[random.Below(6)],
                random.Inclusive(15, 20), random.Inclusive(25, 30), 0, 0, 400000, speed);
        }
    }

    public void Step(Vector3 camera, bool enforceLevelBounds = true)
    {
        _tick++;
        // Native traversal fixes its end before dispatch. Children in later
        // already-allocated slots can update now; growth past that end waits.
        int end = _highest;
        for (int i = 0; i <= end; i++)
        {
            if (_slots[i] is not { } p) continue;
            switch (p.Type)
            {
                case 12: StepFlame(p, enforceLevelBounds); break;
                case 4: StepTrail(p); break;
                case 11: StepExplosion(p, camera); break;
                case 2: StepSecondary(p); break;
                case 8: StepGlow(p); break;
                case 15: StepSpark(p); break;
                case 16: StepSteam(p, camera); break;
            }
        }
        while (_highest >= 0 && _slots[_highest] is null) _highest--;
    }

    private void StepFlame(Particle p, bool bounds)
    {
        p.Angle += p.Spin;
        p.Position += p.Velocity;
        float speed = -p.Velocity.Length();
        // seek_1d(pos=remaining reach, velocity=-speed, target=0, turn=0).
        float error = -p.Reach;
        float acceleration = 48 * Dt * Dt;
        if (speed * error < 0 || error == 0) speed = Approach(speed, 0, acceleration);
        else if (Math.Abs(error) < .5f * speed * speed / acceleration)
        {
            float stop = .5f * speed * speed / acceleration;
            speed = Approach(speed, 0, stop < Math.Abs(error) + Math.Abs(speed) ? acceleration : acceleration * 1.1f);
        }
        p.Reach = Math.Abs(speed) < Math.Abs(error) ? p.Reach + speed : 0;
        p.Velocity = SetLength(p.Velocity, -speed);
        p.Position += new Vector3(0, 0, Dt);
        float t = p.Remaining / (float)p.Total;
        bool longLook = (p.Flags & 1) != 0;
        float end = longLook ? 3 : 1.25f, start = longLook ? 1 : .25f;
        p.RawSize = ((start - end) * t + end) * NativeSizeUnits;
        p.Color = LerpColor(p.EndColor, p.StartColor, t);
        if (p.Remaining < 15) p.Color = LerpColor(p.Color & 0xffffff, p.Color, p.Remaining / 10f);
        if ((bounds && (p.Position.X < 2 || p.Position.X > 1021 || p.Position.Y < 2 || p.Position.Y > 1021 || p.Position.Z < 2 || p.Position.Z > 1021)) || --p.Remaining <= 0)
        { Remove(p); return; }
        if (p.Reach < .01f) p.Remaining--;
    }

    private void StepTrail(Particle p)
    {
        int size = 120 * (p.Total - p.Remaining) / p.Total + 50;
        p.RawSize = size * 1000;
        p.Velocity *= 1 - .02f;
        p.Position += p.Velocity; p.Angle++;
        p.Color = LerpColor(p.EndColor, p.StartColor, p.Remaining / (float)p.Total);
        if (--p.Remaining <= 0) Remove(p);
    }

    private void StepExplosion(Particle p, Vector3 camera)
    {
        p.Angle++;
        if (!p.Smoke)
        {
            p.Position += p.Velocity;
            if ((p.Flags & 4) != 0) p.Velocity += new Vector3(0, 0, 9.8f * Dt * Dt);
            p.Velocity *= (p.Flags & 2) != 0 ? .6f : .8f;
            if (p.Remaining == (int)(p.Total * .85f) && p.Generation < 2 && (p.Flags & 4) == 0)
                for (int i = 0; i < 5; i++)
                {
                    int life = (int)(p.Total * random.Range(.9f, 1.1f));
                    float speed = p.Speed * random.Range(.85f, 1);
                    ExplosionSpark(p.Position, p.Velocity, p.StartColor, p.EndColor, life,
                        p.AuxiliaryLife, p.Generation + 1, p.Flags, p.BaseSize, speed);
                }
            if ((p.Flags & 4) == 0)
            {
                float s = p.BaseSize, life = p.Total;
                float b = (life * 8 - life * 3 * s) / (s * 8 * s + s * 8);
                float a = (life * 3 + b * 8 * s) / (s * -8);
                float root = MathF.Sqrt(b * b - a * 4 * (life - p.Remaining));
                p.RawSize = Math.Max((-b - root) / (a + a), (-b + root) / (a + a));
                float distance = Vector3.Distance(p.Position, camera);
                if (distance < 10) p.RawSize *= distance * .075f + .25f;
            }
            p.Color = (p.Flags & 2) != 0 ? LerpColor(p.StartColor, 0x0fffffffu, p.Remaining / (float)p.Total) :
                LerpColor(p.EndColor, p.StartColor, p.Remaining / (float)p.Total);
            p.AuxiliaryLife = Math.Max(0, p.AuxiliaryLife - 1);
            if (--p.Remaining > 0) return;
            if ((p.Flags & 2) != 0)
            {
                p.Remaining = p.Total *= 3;
                p.Flags = (byte)((p.Flags & ~2) | 4);
                p.Velocity = SetLength(p.Velocity, p.Speed * random.Range(1.6f, 2));
                return;
            }
            p.StartColor = p.EndColor; p.Flags |= 1;
            p.EndColor = p.Blending == Blend.Additive ? 0u : 0x0f2f3f3fu;
            int n = random.Inclusive(p.Total * 2, p.Total * 3);
            if ((p.Flags & 8) != 0) n *= 5;
            p.Remaining = p.Total = n & 255;
            return;
        }
        int full = (int)(p.Total * .75f) + 1, fade = (int)(p.Total * .25f) + 1;
        p.Velocity += new Vector3(0, 0, 3 * Dt * Dt);
        p.Position += p.Velocity; p.Velocity *= .98f;
        if (p.Remaining > full) p.Color = LerpColor(p.EndColor, p.StartColor, (p.Remaining - full) / (float)fade);
        else if (p.Blending == Blend.Additive) p.Remaining = 0;
        else p.Color = (p.Color & 0xffffff) | (uint)(p.Remaining * 15 / full) << 24;
        if (--p.Remaining <= 0) Remove(p);
    }

    public void Remove(Particle p) { if (!IsLive(p)) return; _slots[p.Slot] = null; Count--; }
    public void Clear() { Array.Clear(_slots); _highest = -1; Count = 0; }
    public void RemoveType(int type) { foreach (var p in Live) if (p.Type == type) Remove(p); }
    private static float Approach(float value, float target, float step) => value + Math.Clamp(target - value, -step, step);
    public static Vector3 SetLength(Vector3 vector, float length) => vector.LengthSquared() > 0 ? vector * (length / vector.Length()) : Vector3.Zero;
    private Vector3 Sphere(float minimum, float maximum)
    {
        float r = random.Range(minimum, maximum), a = random.Angle(), b = random.Angle();
        return new(MathF.Cos(a) * MathF.Sin(b) * r, MathF.Sin(a) * MathF.Sin(b) * r, MathF.Cos(b) * r);
    }
    public static uint LerpColor(uint a, uint b, float t)
    {
        uint color = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int x = (int)(a >> shift & 255), y = (int)(b >> shift & 255);
            color |= ((uint)(int)(x + (y - x) * t) & 255) << shift;
        }
        return color;
    }
}
