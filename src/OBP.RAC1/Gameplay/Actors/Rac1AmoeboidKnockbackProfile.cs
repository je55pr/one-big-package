using System.Buffers.Binary;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Source FUN_L00_0025c558 initializes the trooper knockback vector using
/// PVar+0x78 (horizontal) and +0x7c (vertical). Source FUN_L00_0025c698
/// applies +0x70 (gravity) and +0x74 (horizontal deceleration) each tick.
/// Godot contact/grounding remains an explicitly adapted collision solver.
/// </summary>
public static class Rac1AmoeboidKnockbackProfile
{
    public static Rac1MobyReaction.Profile Read(int nativeClass, ReadOnlySpan<byte> pvar)
    {
        if (!Rac1AmoeboidAuthored.IsFamily(nativeClass))
            throw new ArgumentOutOfRangeException(nameof(nativeClass));
        if (pvar.Length != Rac1AmoeboidAuthored.PVarSize)
            throw new InvalidDataException("Amoeboid knockback needs the exact original PVar.");

        float gravity = BinaryPrimitives.ReadSingleLittleEndian(pvar[0x70..]);
        float drag = BinaryPrimitives.ReadSingleLittleEndian(pvar[0x74..]);
        float planar = BinaryPrimitives.ReadSingleLittleEndian(pvar[0x78..]);
        float vertical = BinaryPrimitives.ReadSingleLittleEndian(pvar[0x7c..]);
        if (!(gravity > 0 && drag >= 0 && planar >= 0 && vertical >= 0) ||
            !float.IsFinite(gravity) || !float.IsFinite(drag) ||
            !float.IsFinite(planar) || !float.IsFinite(vertical))
            throw new InvalidDataException("Native Amoeboid knockback parameters are invalid.");
        return new(gravity, drag, planar, vertical);
    }
}
