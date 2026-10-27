using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using OBP.Core;
using OBP.RAC3.Level;
using M = OBP.RAC3.Gameplay.Rac3ParticleMath;

namespace OBP.RAC3.Gameplay;

public readonly record struct Rac3ParticleOwner(ObpSourceRef Source, long Generation);
public enum Rac3ParticleRecipe { Constructor97, ShockCannonCharging, ShockCannonBeamGlow }
public readonly record struct Rac3Particle97Spawn(Vector4 Position, Vector3 Velocity, int Lifespan, uint Rgb,
    int RotationSpeed, byte InitialOpacity, byte EndOpacity, float InitialSize, float EndSize, float Gravity, int TextureFrame = 0);

/// <summary>TABLE1 type-97 constructor/callback 0x46dcb8/0x46de20. Step is one native NTSC update.</summary>
public sealed class Rac3ParticleSession
{
    public const int NativeCapacity = 2048;
    private const float CannonGravity = -1f / 3600;
    private readonly Rac3ParticleAssets _assets;
    private readonly Rac3ParticleRandom _random;
    private readonly Particle?[] _slots;
    private readonly IReadOnlyDictionary<int, ObpSourceRef> _textureSources;
    private int _highest = -1;
    private long _nextIdentity;
    public int Count { get; private set; }
    public static bool SupportsType(int type) => type == 97;

    public Rac3ParticleSession(Rac3ParticleAssets assets, Rac3ParticleRandom random, int capacity = NativeCapacity)
    {
        if (assets.Source.Game != ObpSourceGame.Rac3 || assets.Source.BuildId != Rac3Authority.Primary.BuildId || assets.Source.LevelId != "TABLE1")
            throw new ArgumentException("This UYA particle slice requires the qualified original TABLE1 bank.", nameof(assets));
        if (capacity is < 1 or > NativeCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        _assets = assets; _random = random; _slots = new Particle[capacity];
        _textureSources = assets.Textures.Keys.ToDictionary(id => id, id => assets.Source with
        { AssetKind = Rac3ParticleAssets.AssetKind, OriginalId = id.ToString(CultureInfo.InvariantCulture) });
    }

    public sealed class Particle
    {
        internal Particle(Rac3ParticleOwner owner, ObpSourceRef textureSource) => (Owner, TextureSource) = (owner, textureSource);
        public Rac3ParticleOwner Owner { get; }
        public int Slot { get; internal init; }
        public long Identity { get; internal init; }
        public Rac3ParticleRecipe Recipe { get; internal set; }
        public int Type => 97;
        public byte Blend => 0x48;
        public byte DepthRange => 0x44;
        public int Texture { get; internal set; }
        public ObpSourceRef TextureSource { get; internal set; }
        public Vector3 Position { get; internal set; }
        public float PositionW { get; internal init; }
        public Vector3 Velocity { get; internal set; }
        public float NativeSize { get; internal set; }
        public uint Color { get; internal set; }
        public byte Angle { get; internal set; }
        public int Remaining { get; internal set; }
        public int Total { get; internal init; }
        internal float SizeStep, Gravity;
        internal byte OpacityStep, Spin;

        /// <summary>Live-record diagnostic layout; the callback value is TABLE1-qualified, never a managed pointer.</summary>
        public byte[] CopyNativeRecord()
        {
            byte[] b = new byte[64]; b[0] = 97; b[2] = checked((byte)Texture); b[3] = Blend;
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), Color); b[8] = Angle; b[9] = DepthRange;
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(10), checked((short)Remaining));
            void F(int at, float n) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), n);
            F(12, NativeSize); F(16, Position.X); F(20, Position.Y); F(24, Position.Z); F(28, PositionW);
            F(32, Velocity.X); F(36, Velocity.Y); F(40, Velocity.Z); F(44, Gravity); F(48, SizeStep);
            b[52] = OpacityStep; b[53] = Spin; BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(60), 0x46de20);
            return b;
        }
    }

    public IEnumerable<Particle> Live { get { for (int i = 0; i <= _highest; i++) if (_slots[i] is { } p) yield return p; } }
    public bool IsLive(Particle p) => (uint)p.Slot < _slots.Length && ReferenceEquals(_slots[p.Slot], p);

    public Particle? Spawn97(Rac3ParticleOwner owner, Rac3Particle97Spawn p)
    {
        RequireOwner(owner);
        if (p.Lifespan is < 1 or > short.MaxValue || !Finite(p.Position) || !Finite(p.Velocity) ||
            !float.IsFinite(p.InitialSize) || !float.IsFinite(p.EndSize) || !float.IsFinite(p.Gravity))
            throw new ArgumentException("UYA type-97 inputs exceed the admitted finite, positive-timer domain.", nameof(p));
        int texture = RequireTexture(97, p.TextureFrame);
        int free = Array.FindIndex(_slots, p => p is null);
        if (free < 0) return null; // Unlike recipe draws, constructor angle is not consumed on failure.
        var particle = new Particle(owner, _textureSources[texture])
        {
            Slot = free,
            Identity = ++_nextIdentity,
            Texture = texture,
            Position = new(p.Position.X, p.Position.Y, p.Position.Z),
            PositionW = p.Position.W,
            Velocity = p.Velocity,
            NativeSize = p.InitialSize,
            Color = p.Rgb & 0xffffff | (uint)p.InitialOpacity << 24,
            Angle = (byte)_random.Below(255),
            Remaining = p.Lifespan,
            Total = p.Lifespan,
            // UYA does NOT replace life with delta/quotient as GC's Museum type 85 does.
            OpacityStep = unchecked((byte)((p.EndOpacity - p.InitialOpacity) / p.Lifespan)),
            Spin = unchecked((byte)p.RotationSpeed),
            Gravity = p.Gravity,
            SizeStep = M.Divide(M.Subtract(p.EndSize, p.InitialSize), M.FromInt(p.Lifespan))
        };
        _slots[free] = particle; Count++; _highest = Math.Max(_highest, free); return particle;
    }

    /// <summary>Complete pre-ready charge recipe 0x385e80..0x385f50: two type-97 particles.</summary>
    public Emission EmitShockCannonCharging(Rac3ParticleOwner owner, int nativeChargeAge, Vector4 nativeMuzzle)
    {
        if (nativeChargeAge is < 1 or >= 79) throw new NotSupportedException("Only native charge ages 1..78 use this recovered recipe.");
        RequireCannonResources(owner); RequireFinite(nativeMuzzle);
        int emitted = 0; uint rgb = (uint)((255 - nativeChargeAge) << 16 | (128 + nativeChargeAge) << 8 | 0x20);
        for (int i = 0; i < 2; i++)
        {
            int life = _random.Integer(10, 20), spin = _random.Integer(-1, -3);
            var p = Spawn97(owner, new(nativeMuzzle, Vector3.Zero, life, rgb, spin, (byte)(nativeChargeAge / 3), 0,
                21000, 115500, CannonGravity));
            if (p is not null) { PatchCannon(p, Rac3ParticleRecipe.ShockCannonCharging); emitted++; }
        }
        return new(2, emitted);
    }

    /// <summary>0x3865cc..0x38675c: five random points along the charged shot, then one muzzle glow.</summary>
    public Emission EmitShockCannonBeamGlow(Rac3ParticleOwner owner, Vector4 nativeMuzzle, Vector4 nativeBeamEnd)
    {
        RequireCannonResources(owner); RequireFinite(nativeMuzzle); RequireFinite(nativeBeamEnd);
        int emitted = 0;
        for (int i = 0; i < 5; i++)
        {
            var position = M.Lerp(nativeMuzzle, nativeBeamEnd, _random.Float(0, 1));
            if (CannonBeamParticle(owner, position, 168000, 315000)) emitted++;
        }
        if (CannonBeamParticle(owner, nativeMuzzle, 63000.00390625f, 210000)) emitted++;
        return new(6, emitted);
    }

    private bool CannonBeamParticle(Rac3ParticleOwner owner, Vector4 position, float initial, float end)
    {
        int life = _random.Integer(10, 18), spin = _random.Integer(-1, -3);
        var p = Spawn97(owner, new(position, Vector3.Zero, life, 0x804020, spin, 80, 0, initial, end, CannonGravity));
        if (p is null) return false;
        PatchCannon(p, Rac3ParticleRecipe.ShockCannonBeamGlow); return true;
    }
    private void PatchCannon(Particle p, Rac3ParticleRecipe recipe)
    {
        p.Texture = RequireTexture(104, _random.Integer(0, 3)); p.TextureSource = _textureSources[p.Texture]; p.Recipe = recipe;
    }

    internal void RequireCannonResources(Rac3ParticleOwner owner)
    {
        RequireOwner(owner); RequireTexture(97, 0);
        for (int frame = 0; frame < 4; frame++) RequireTexture(104, frame);
    }
    private int RequireTexture(int definition, int frame)
    {
        int id = _assets.Definitions.Texture(definition, frame);
        if (!_textureSources.ContainsKey(id)) throw new InvalidDataException("UYA particle source texture is unavailable.");
        return id;
    }
    private void RequireOwner(Rac3ParticleOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner.Source);
        if (owner.Generation < 0 || owner.Source.Game != _assets.Source.Game || owner.Source.BuildId != _assets.Source.BuildId || owner.Source.LevelId != _assets.Source.LevelId)
            throw new InvalidOperationException("UYA effect owners cannot cross source/build/level banks or use an invalid generation.");
    }

    /// <summary>World-frame conversion and directional gravity are shared native globals, not a GC per-record context field.</summary>
    public void Step(bool movingFrameActive = false, bool directionalGravityActive = false)
    {
        if (movingFrameActive || directionalGravityActive)
            throw new NotSupportedException("UYA 0x4c6ec8/0x4c41e8 moving-frame or directional-gravity adapters are not admitted.");
        int end = _highest;
        for (int i = 0; i <= end; i++)
        {
            if (_slots[i] is not { } p) continue;
            if (--p.Remaining <= 0) { Remove(p); continue; }
            p.Color = p.Color & 0xffffff | (uint)unchecked((byte)((p.Color >> 24) + p.OpacityStep)) << 24;
            p.Position = M.Add(p.Position, p.Velocity); // Original VADD.xyz; W remains copied.
            p.Velocity = new(p.Velocity.X, p.Velocity.Y, M.Subtract(p.Velocity.Z, p.Gravity));
            p.Angle = unchecked((byte)(p.Angle + p.Spin)); p.NativeSize = M.Add(p.NativeSize, p.SizeStep);
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
        foreach (var p in Live) destination.Add(new(p.Slot, p.Identity, p.Owner, p.Recipe, p.TextureSource,
            p.Texture, p.Position, p.NativeSize, p.Color, p.Angle, p.Blend, p.DepthRange));
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Vector4 v) => Finite(new Vector3(v.X, v.Y, v.Z)) && float.IsFinite(v.W);
    private static void RequireFinite(Vector4 v) { if (!Finite(v)) throw new ArgumentException("UYA particle positions must be finite.", nameof(v)); }
    public readonly record struct Emission(int Attempted, int Emitted);
    public readonly record struct SpriteSnapshot(int Slot, long Identity, Rac3ParticleOwner Owner, Rac3ParticleRecipe Recipe,
        ObpSourceRef TextureSource, int Texture, Vector3 Position, float NativeSize, uint Color, byte Angle, byte Blend, byte DepthRange);
}
