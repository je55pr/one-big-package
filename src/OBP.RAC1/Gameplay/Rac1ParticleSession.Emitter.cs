using System.Numerics;
using OBP.Core;
using M = OBP.RAC1.Gameplay.Rac1ParticleEmitterMath;

namespace OBP.RAC1.Gameplay;

public sealed partial class Rac1ParticleSession
{
    /// <summary>Only these native sprite families have managed simulation in this pool.</summary>
    public static bool SupportsType(int type) => type is 2 or 4 or 6 or 8 or 11 or 12 or 15 or 16;

    /// <summary>
    /// Class-27 update 0x2bd100, before Step. Novalis culling freezes the burst countdown.
    /// Each attempted particle draws eight samples even when the pool is full.
    /// Adapted with ISC attribution in THIRD_PARTY_NOTICES.md; retail instructions remain authority.
    /// </summary>
    public Rac1ParticleEmission Emit(Rac1ParticleEmitter owner, Rac1ParticleEmitterView? previousView)
    {
        if (owner.IsRetired) return new(Rac1ParticleEmissionStatus.Retired, 0, 0);
        if (!owner.EmissionEnabled) return new(Rac1ParticleEmissionStatus.Stopped, 0, 0);
        if (owner.Source.LevelId == "LEVEL1" && (previousView is null || !previousView.Sees(owner.Position)))
            return new(Rac1ParticleEmissionStatus.Culled, 0, 0);
        if (owner.BurstCountdown >= 1)
        {
            owner.BurstCountdown--;
            return new(Rac1ParticleEmissionStatus.Countdown, 0, 0);
        }
        var p = owner.Parameters;
        // Fail before consuming a burst/RNG if the caller omitted the source texture definition.
        if (p.BurstCount != 0 && (!assets.ParticleChoices.TryGetValue(p.TextureDefinition, out var choices) || choices.Length == 0))
            throw new InvalidDataException("Class-27 requested an absent native texture definition.");
        owner.BurstCountdown = p.BurstInterval;
        int created = 0;
        for (int i = 0; i < p.BurstCount; i++)
        {
            Vector3 range = p.OffsetRange;
            Vector3 offset = new(EmitterRange(-range.X, range.X), EmitterRange(-range.Y, range.Z), EmitterRange(-range.Z, range.Z));
            if ((p.Flags & 0x2000) != 0) offset = M.Rotate(offset, owner.Rotation);
            Vector3 position = M.Add(owner.Position, offset);
            Vector3 low = p.VelocityMinimumOrBase, high = p.VelocityMaximumOrEulerRange, velocity;
            if ((p.Flags & 0x1000) == 0)
            {
                velocity = new(EmitterRange(low.X, high.X), EmitterRange(low.Y, high.Y), EmitterRange(low.Z, high.Z));
                if ((p.Flags & 0x4000) != 0) velocity = M.Rotate(velocity, owner.Rotation);
            }
            else
            {
                Vector3 euler = new(M.Div(M.Mul(EmitterRange(-high.X, high.X), MathF.PI), 180),
                    M.Div(M.Mul(EmitterRange(-high.Y, high.Y), MathF.PI), 180),
                    M.Div(M.Mul(EmitterRange(-high.Z, high.Z), MathF.PI), 180));
                if ((p.Flags & 0x4000) != 0)
                {
                    float x = M.AddAngle(euler.X, owner.Rotation.X);
                    // Retail passes the NEW x to both subsequent calls, not y/z.
                    euler = new(x, M.AddAngle(x, owner.Rotation.Y), M.AddAngle(x, owner.Rotation.Z));
                }
                velocity = M.Rotate(low, euler);
            }
            float size = EmitterRange(p.InitialSizeMinimum, p.InitialSizeMaximum);
            float angle = EmitterRange(p.AngleMinimum, p.AngleMaximum);
            var record = Allocate(6, p.TextureDefinition, 0, (p.DrawFlags & 1) != 0 ? Blend.Additive : Blend.Alpha,
                position, (byte)(((p.DrawFlags >> 5) << 4) + 4));
            if (record is null) continue;
            record.Emitter = owner; record.Velocity = velocity; record.RawSize = size;
            record.EmitterAlpha = p.InitialAlpha; record.EmitterAngle = angle;
            record.Remaining = record.Total = p.Lifetime;
            record.Color = M.Pack(p.InitialColor, p.InitialAlpha);
            record.RedLow = EmitterLow(p.InitialColor.X); record.GreenLow = EmitterLow(p.InitialColor.Y); record.BlueLow = EmitterLow(p.InitialColor.Z);
            record.Angle = unchecked((byte)(int)M.Mul(angle, 255));
            created++;
        }
        return new(created == 0 && p.BurstCount != 0 ? Rac1ParticleEmissionStatus.PoolFull : Rac1ParticleEmissionStatus.Emitted,
            p.BurstCount, created);
    }

    private float EmitterRange(float minimum, float maximum) => M.Add(minimum, M.Mul(M.Mul(random.Next15(), M.Sub(maximum, minimum)), 1f / 32768));
    private static byte EmitterLow(float value) => unchecked((byte)(int)M.Mul(value, 65535));

    private void StepEmitter(Particle record)
    {
        var owner = record.Emitter!;
        if (owner.IsRetired) { Remove(record); return; }
        var p = owner.Parameters;
        uint flags = p.Flags;
        record.Position = M.Add(record.Position, record.Velocity);
        if ((flags & 2) != 0)
        {
            float Channel(int shift, byte low, float step, float minimum, float maximum, byte direction)
            {
                float value = M.Div(((record.Color >> shift & 255) << 8) | low, 65535);
                value = (record.EmitterDirections & direction) != 0 ? M.Sub(value, step) : M.Add(value, step);
                if (value > maximum)
                {
                    value = maximum;
                    if ((flags & ((uint)direction << 4)) != 0) record.EmitterDirections ^= direction;
                }
                if (value < minimum)
                {
                    value = minimum;
                    if ((flags & direction) != 0) record.EmitterDirections ^= direction;
                }
                return value;
            }
            Vector3 step = p.ColorStep, min = p.ColorMinimum, max = p.ColorMaximum;
            Vector3 rgb = new(Channel(0, record.RedLow, step.X, min.X, max.X, 0x10),
                Channel(8, record.GreenLow, step.Y, min.Y, max.Y, 0x20), Channel(16, record.BlueLow, step.Z, min.Z, max.Z, 0x40));
            record.RedLow = EmitterLow(rgb.X); record.GreenLow = EmitterLow(rgb.Y); record.BlueLow = EmitterLow(rgb.Z);
            record.Color = M.Pack(rgb, record.EmitterAlpha);
        }
        else if ((flags & 4) != 0)
            record.Color = (record.Color & 0xffffff) | unchecked((uint)(int)M.Mul(record.EmitterAlpha, 255) << 24);
        if ((flags & 4) != 0)
        {
            float alpha = (record.EmitterDirections & 0x80) != 0 ? M.Sub(record.EmitterAlpha, p.AlphaStep) : M.Add(record.EmitterAlpha, p.AlphaStep);
            if (alpha > p.AlphaMaximum) { alpha = p.AlphaMaximum; if ((flags & 0x800) != 0) record.EmitterDirections ^= 0x80; }
            if (alpha < p.AlphaMinimum) { alpha = p.AlphaMinimum; if ((flags & 0x80) != 0) record.EmitterDirections ^= 0x80; }
            record.EmitterAlpha = alpha;
        }
        record.Angle = unchecked((byte)(int)M.Mul(record.EmitterAngle, 255));
        if ((flags & 8) != 0)
        {
            Vector3 acceleration = p.Acceleration;
            if ((flags & 0x8000) != 0) acceleration = M.Rotate(acceleration, owner.Rotation);
            record.Velocity = M.Limit(M.Add(record.Velocity, acceleration), p.SpeedLimit);
        }
        record.RawSize = Math.Clamp(M.Add(record.RawSize, p.SizeStep), p.SizeMinimum, p.SizeMaximum);
        record.EmitterAngle = M.Add(record.EmitterAngle, p.AngularStep);
        Vector3 pos = record.Position, lo = p.BoundsMinimum, hi = p.BoundsMaximum;
        if (record.EmitterAlpha <= 0 || --record.Remaining <= 0 ||
            pos.X < lo.X || pos.Y < lo.Y || pos.Z < lo.Z || pos.X > hi.X || pos.Y > hi.Y || pos.Z > hi.Z)
            Remove(record);
    }

    /// <summary>Copies deterministic sprite facts, in native slot order; no meshes or game-event recipes.</summary>
    public IReadOnlyList<SpriteSnapshot> CaptureSprites() => Live.Select(p => new SpriteSnapshot(p.Slot, p.Identity,
        p.Emitter?.Source, p.Emitter?.Owner, p.Texture, p.Blending, p.DepthRange, p.Position, p.Size, p.Color, p.Angle, p.Remaining)).ToArray();

    public readonly record struct SpriteSnapshot(int Slot, long Identity, ObpSourceRef? Source, Rac1MobyRuntimeKey? Owner,
        int Texture, Blend Blending, byte DepthRange, Vector3 Position, float Size, uint Color, byte Angle, int Remaining);
}
