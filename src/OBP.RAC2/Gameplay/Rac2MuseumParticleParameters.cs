using System.Buffers.Binary;
using System.Numerics;
using OBP.RAC2.Level;

namespace OBP.RAC2.Gameplay;

/// <summary>The 0x50-byte class-4887 PVars. Values retain native Z-up, per-tick units.</summary>
public sealed class Rac2MuseumParticleParameters
{
    public const int ByteSize = 0x50;
    private readonly byte[] _bytes;
    private Rac2MuseumParticleParameters(byte[] bytes) => _bytes = bytes;

    public static Rac2MuseumParticleParameters Parse(ReadOnlySpan<byte> pvars)
    {
        if (pvars.Length != ByteSize) throw new InvalidDataException("Museum class-4887 requires its complete 0x50-byte PVars.");
        var result = new Rac2MuseumParticleParameters(pvars.ToArray());
        foreach (int offset in new[] { 0, 4, 8, 0xc, 0x2c, 0x30, 0x34 })
            if (!float.IsFinite(result.Float(offset))) throw new InvalidDataException("Museum particle parameters must be finite.");
        if (result.Lifespan is < 1 or > 1000 || result.RotationSpeed is < -1000 or > 1000 ||
            result.InitialOpacity is < 0 or > 127 || result.EndOpacity is < 0 or > 127 ||
            result.Red is < 0 or > 255 || result.Green is < 0 or > 255 || result.Blue is < 0 or > 255 ||
            result.InitialScale is < 0 or > 1000 || result.EndScale is < 0 or > 1000 ||
            result.Gravity is < -1000 or > 1000 || result.TextureDefinition is < 0 or > 103 ||
            result.TextureFrame is < 0 or > 20 || result.LowSpawnTimer is < 0 or > 1000 || result.HighSpawnTimer is < 0 or > 1000 ||
            MathF.Abs(result.Velocity.X) > 1000 || MathF.Abs(result.Velocity.Y) > 1000 || MathF.Abs(result.Velocity.Z) > 1000)
            throw new InvalidDataException("Museum particle parameters exceed the recovered editor control ranges.");
        return result;
    }

    public Vector3 Velocity => new(Float(0), Float(4), Float(8));
    public float AuthoredCountdown => Float(0xc);
    public int Lifespan => Int(0x10);
    public int Red => Int(0x14);
    public int Green => Int(0x18);
    public int Blue => Int(0x1c);
    public int RotationSpeed => Int(0x20);
    public int InitialOpacity => Int(0x24);
    public int EndOpacity => Int(0x28);
    public float InitialScale => Float(0x2c);
    public float EndScale => Float(0x30);
    public float Gravity => Float(0x34);
    public int NativeContext => Int(0x38);
    public int TextureDefinition => Int(0x40);
    public int TextureFrame => Int(0x44);
    public short LowSpawnTimer => BinaryPrimitives.ReadInt16LittleEndian(_bytes.AsSpan(0x4c));
    public short HighSpawnTimer => BinaryPrimitives.ReadInt16LittleEndian(_bytes.AsSpan(0x4e));
    public byte[] CopyNativeParameters() => (byte[])_bytes.Clone();

    /// <summary>0x413270/0x4132dc/0x4133f4: integer/short steps, float steps of 0.01, then native menu clamps.</summary>
    public Rac2MuseumParticleParameters Edit(Rac2MuseumEffectControls.Parameter control, int steps)
    {
        int offset = control.PVarOffset;
        bool floating = offset is 0 or 4 or 8 or 0x2c or 0x30 or 0x34;
        bool integer = offset is 0x10 or 0x14 or 0x18 or 0x1c or 0x20 or 0x24 or 0x28 or 0x40 or 0x44;
        bool shortInteger = offset is 0x4c or 0x4e;
        if (!(floating && control.Kind == Rac2MuseumEffectControls.Storage.Float32 ||
            integer && control.Kind == Rac2MuseumEffectControls.Storage.Int32 ||
            shortInteger && control.Kind == Rac2MuseumEffectControls.Storage.Int16) || control.Minimum > control.Maximum)
            throw new ArgumentException("This control is not a recovered class-4887 parameter.", nameof(control));
        byte[] bytes = CopyNativeParameters();
        if (floating)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), Math.Clamp(
                Rac2ParticleMath.Add(Float(offset), Rac2ParticleMath.Multiply(Rac2ParticleMath.FromInt(steps), .01f)), control.Minimum, control.Maximum));
        else
        {
            int value = shortInteger ? unchecked((short)(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset)) + steps)) : unchecked(Int(offset) + steps);
            value = Math.Clamp(value, control.Minimum, control.Maximum);
            if (shortInteger) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), checked((short)value));
            else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        }
        return Parse(bytes);
    }

    internal void RequireSupported()
    {
        if (NativeContext != 0)
            throw new NotSupportedException("GC type-85 moving-context conversion requires its source-owned context adapter; this slice admits world-space context 0.");
    }

    internal Rac2MuseumParticleParameters NormalizeSpawnTimers()
    {
        if (LowSpawnTimer <= HighSpawnTimer) return this;
        byte[] bytes = CopyNativeParameters();
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0x4c), HighSpawnTimer);
        return new(bytes);
    }

    private int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));
    private float Float(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(offset));
}
