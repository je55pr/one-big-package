using System.Buffers.Binary;
using System.Numerics;
using OBP.Core;
using OBP.RAC2.Level;
using M = OBP.RAC2.Gameplay.Rac2ParticleMath;

namespace OBP.RAC2.Gameplay;

/// <summary>Arguments to the shared GC type-85 constructor; sizes are already in native particle units.</summary>
public readonly record struct Rac2Particle85Spawn(Vector3 Position, Vector3 Velocity, int Lifespan, uint Rgb,
    int RotationSpeed, byte InitialOpacity, byte EndOpacity, float InitialSize, float EndSize, float Gravity, int NativeContext = 0);

/// <summary>GC linear sprites: Museum type 85 and loaded Oozla type 97. One Step is one NTSC tick.</summary>
public sealed class Rac2ParticleSession(Rac2ParticleAssets assets, Rac2ParticleRandom random, int capacity = 2048)
{
    public const int NativeCapacity = 2048;
    public const float EditorSizeUnits = 210000;
    /// <summary>Linear constructors recovered by this session; each caller still needs its own recipe admission.</summary>
    public static bool SupportsType(int type) => type is 85 or 97;

    public sealed class Particle
    {
        internal Particle(ObpSourceRef source, ObpSourceRef textureSource) => (Source, TextureSource) = (source, textureSource);
        public int Slot { get; internal init; }
        public long Identity { get; internal init; }
        public ObpSourceRef Source { get; }
        public int Type { get; internal init; } = 85;
        public ObpSourceRef TextureSource { get; internal set; }
        public int Texture { get; internal set; }
        public byte Blend => 0x48;
        public byte DepthRange { get; internal set; } = 0x44;
        public uint Color { get; internal set; }
        public byte Angle { get; internal set; }
        public int Remaining { get; internal set; }
        public int Total { get; internal init; }
        public Vector3 Position { get; internal set; }
        public Vector3 Velocity { get; internal set; }
        public float NativeSize { get; internal set; }
        internal float SizeStep, Gravity;
        internal byte OpacityStep, Spin;

        /// <summary>Source-layout comparison; callback identities name Museum type 85 / Oozla type 97, not live runtime pointers.</summary>
        public byte[] CopyNativeRecord()
        {
            byte[] record = new byte[64];
            record[0] = checked((byte)Type); record[2] = checked((byte)Texture); record[3] = Blend;
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), Color);
            record[8] = Angle; record[9] = DepthRange;
            BinaryPrimitives.WriteInt16LittleEndian(record.AsSpan(0xa), checked((short)Remaining));
            void F(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(at), value);
            F(0xc, NativeSize); F(0x10, Position.X); F(0x14, Position.Y); F(0x18, Position.Z);
            F(0x20, Velocity.X); F(0x24, Velocity.Y); F(0x28, Velocity.Z); F(0x2c, Gravity); F(0x30, SizeStep);
            record[0x34] = OpacityStep; record[0x35] = Spin;
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x3c), Type == 85 ? 0x33eaa0u : 0x3356a8u);
            return record;
        }
    }

    private readonly Particle?[] _slots = capacity is > 0 and <= NativeCapacity ? new Particle[capacity] : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly IReadOnlyDictionary<int, ObpSourceRef> _textureSources = assets.Textures.Keys.ToDictionary(id => id,
        id => assets.Source with { AssetKind = Rac2ParticleAssets.AssetKind, OriginalId = id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    private int _highest = -1;
    private long _nextIdentity;
    public int Count { get; private set; }
    internal long ReserveIdentity() => ++_nextIdentity;
    public IEnumerable<Particle> Live { get { for (int i = 0; i <= _highest; i++) if (_slots[i] is { } p) yield return p; } }
    public bool IsLive(Particle p) => (uint)p.Slot < _slots.Length && ReferenceEquals(_slots[p.Slot], p);

    public Emission Emit(Rac2MuseumParticleEmitter owner)
    {
        if (!owner.EmissionEnabled) return new(EmissionStatus.Stopped, null);
        RequireSource(owner.Source);
        var p = owner.Parameters;
        int texture = assets.Definitions.Texture(p.TextureDefinition, p.TextureFrame);
        if (!assets.Textures.ContainsKey(texture)) throw new InvalidDataException("The selected GC Part texture has not been admitted.");
        // Native startup state 0 seeds the countdown even for equal interval endpoints.
        if (!owner.Initialized)
        {
            owner.NormalizeSpawnTimers(); p = owner.Parameters;
            owner.Countdown = random.Inclusive(p.LowSpawnTimer, p.HighSpawnTimer); owner.Initialized = true;
        }
        float before = owner.Countdown;
        owner.Countdown = M.Subtract(before, 1);
        if (before != 0) return new(EmissionStatus.Countdown, null);
        owner.NormalizeSpawnTimers(); p = owner.Parameters;
        owner.Countdown = random.Inclusive(p.LowSpawnTimer, p.HighSpawnTimer);
        var record = Spawn85(owner.Source, new(owner.Position, p.Velocity, p.Lifespan,
            (uint)(p.Red | p.Green << 8 | p.Blue << 16), p.RotationSpeed, (byte)p.InitialOpacity, (byte)p.EndOpacity,
            M.Multiply(p.InitialScale, EditorSizeUnits), M.Multiply(p.EndScale, EditorSizeUnits), p.Gravity, p.NativeContext));
        if (record is null) return new(EmissionStatus.PoolFull, null);
        // These caller-owned overrides are separate from the common type-85 constructor.
        record.Texture = texture; record.TextureSource = _textureSources[texture]; record.DepthRange = 0xff;
        return new(EmissionStatus.Emitted, record);
    }

    public Particle? Spawn85(ObpSourceRef source, Rac2Particle85Spawn p) => SpawnLinear(source, p, 85, 0);

    /// <summary>Oozla 0x335530/0x3356A8: same linear callback, original lifetime and definition-97 frame.</summary>
    internal Particle? Spawn97(ObpSourceRef source, Rac2Particle85Spawn p, int frame) => SpawnLinear(source, p, 97, frame);

    private Particle? SpawnLinear(ObpSourceRef source, Rac2Particle85Spawn p, int type, int frame)
    {
        RequireSource(source);
        if (p.NativeContext != 0) throw new NotSupportedException("GC moving-context linear particles require a native context adapter.");
        if (p.Lifespan is < 1 or > short.MaxValue || !Finite(p.Position) || !Finite(p.Velocity) ||
            !float.IsFinite(p.InitialSize) || !float.IsFinite(p.EndSize) || !float.IsFinite(p.Gravity))
            throw new ArgumentException("GC linear particle arguments exceed the admitted finite, positive-timer domain.", nameof(p));
        int texture = assets.Definitions.Texture(type, frame);
        if (!_textureSources.ContainsKey(texture)) throw new InvalidDataException("GC linear particle's Part texture has not been admitted.");
        int free = Array.FindIndex(_slots, slot => slot is null);
        // Constructor returns on a full pool before drawing its random angle.
        if (free < 0) return null;
        int opacityDelta = p.EndOpacity - p.InitialOpacity;
        int step = opacityDelta / p.Lifespan;
        int lifespan = type == 97 || step == 0 ? p.Lifespan : opacityDelta / step;
        var record = new Particle(source, _textureSources[texture])
        {
            Slot = free,
            Type = type,
            Identity = ReserveIdentity(),
            Texture = texture,
            Position = p.Position,
            Velocity = p.Velocity,
            NativeSize = p.InitialSize,
            Color = p.Rgb & 0xffffff | (uint)p.InitialOpacity << 24,
            Angle = (byte)random.Below(255),
            Remaining = lifespan,
            Total = lifespan,
            OpacityStep = unchecked((byte)step),
            Spin = unchecked((byte)p.RotationSpeed),
            Gravity = p.Gravity,
            SizeStep = M.Divide(M.Subtract(p.EndSize, p.InitialSize), M.FromInt(lifespan))
        };
        _slots[free] = record; Count++; _highest = Math.Max(_highest, free);
        return record;
    }

    public void Step()
    {
        int end = _highest;
        for (int i = 0; i <= end; i++)
        {
            if (_slots[i] is not { } p) continue;
            // GC's callback expires BEFORE colour/motion/size; no final movement at timer zero.
            if (--p.Remaining <= 0) { Remove(p); continue; }
            byte alpha = unchecked((byte)((p.Color >> 24) + p.OpacityStep));
            p.Color = p.Color & 0xffffff | (uint)alpha << 24;
            p.Position = M.Add(p.Position, p.Velocity);
            p.Velocity = new(p.Velocity.X, p.Velocity.Y, M.Subtract(p.Velocity.Z, p.Gravity));
            p.Angle = unchecked((byte)(p.Angle + p.Spin));
            p.NativeSize = M.Add(p.NativeSize, p.SizeStep);
        }
    }

    public void Remove(Particle p)
    {
        if (!IsLive(p)) return;
        _slots[p.Slot] = null; Count--;
        while (_highest >= 0 && _slots[_highest] is null) _highest--;
    }
    public void Clear() { Array.Clear(_slots); _highest = -1; Count = 0; }

    public void CaptureSprites(List<SpriteSnapshot> destination)
    {
        destination.Clear();
        foreach (var p in Live) destination.Add(new(p.Slot, p.Identity, p.Source, p.TextureSource,
            p.Texture, p.Position, p.NativeSize, p.Color, p.Angle, p.Blend, p.DepthRange));
    }

    private void RequireSource(ObpSourceRef source)
    {
        if (source.Game != ObpSourceGame.Rac2 || source.BuildId != Rac2Authority.Primary.BuildId ||
            source.Game != assets.Source.Game || source.BuildId != assets.Source.BuildId || source.LevelId != assets.Source.LevelId)
            throw new InvalidOperationException("GC particle identities cannot cross a source/build/level texture bank.");
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public readonly record struct SpriteSnapshot(int Slot, long Identity, ObpSourceRef EmitterSource, ObpSourceRef TextureSource,
        int Texture, Vector3 Position, float NativeSize, uint Color, byte Angle, byte Blend, byte DepthRange);
    public enum EmissionStatus { Emitted, Countdown, PoolFull, Stopped }
    public readonly record struct Emission(EmissionStatus Status, Particle? Particle);
}
