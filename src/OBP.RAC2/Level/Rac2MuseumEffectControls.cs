using System.Buffers.Binary;
using System.Text;

namespace OBP.RAC2.Level;

/// <summary>
/// The Museum particle editor's actual menu-registration arguments, read from
/// LEVEL21 (engine30). This is evidence for GC parameters, not a claim that the
/// editor or its particle update is the same as R&C1's.
/// </summary>
public static class Rac2MuseumEffectControls
{
    public const int FileLevel = 21;
    public const uint ParticleEditorStart = 0x4187d4;
    public const uint ParticleEditorEnd = 0x4189f8;
    public const uint AddParameterFunction = 0x413528;
    public enum Storage { Int32 = 0, Float32 = 1, Int16 = 3 }
    public sealed record Parameter(string Name, int PVarOffset, Storage Kind, int Minimum, int Maximum, uint CallAddress);
    private readonly record struct Value(int Number, bool Relative = false);

    public static IReadOnlyList<Parameter> ReadParticleEditor(Rac2LevelOverlay.Image overlay)
    {
        var values = new Value?[32];
        values[0] = new(0); values[16] = new(0, true); // s0 is the owning Moby's PVar.
        var result = new List<Parameter>();
        uint Word(uint pc) => BinaryPrimitives.ReadUInt32LittleEndian(overlay.ReadVirtual(pc, 4));
        for (uint pc = ParticleEditorStart; pc < ParticleEditorEnd; pc += 4)
        {
            uint word = Word(pc);
            if (word >> 26 == 3)
            {
                uint target = (pc & 0xf0000000) | (word & 0x3ffffff) << 2;
                if (target != AddParameterFunction) throw new InvalidDataException("Museum control registration called an unrecognized routine.");
                Assign(Word(pc + 4));
                Value address = values[5] ?? throw new InvalidDataException("Museum control lacks a bounded PVar reference.");
                Value label = values[6] ?? throw new InvalidDataException("Museum control lacks a label.");
                int kind = Absolute(7), minimum = Absolute(8), maximum = Absolute(9);
                if (!address.Relative || address.Number is < 0 or > 0x4e || label.Relative ||
                    kind is not (0 or 1 or 3) || minimum > maximum)
                    throw new InvalidDataException("Museum control registration has invalid argument forms.");
                result.Add(new(ReadLabel((uint)label.Number), address.Number, (Storage)kind, minimum, maximum, pc));
                for (int r = 2; r <= 15; r++) values[r] = null;
                pc += 4;
            }
            else Assign(word);
        }
        if (result.Count != 17) throw new InvalidDataException("Museum particle-editor registration count differs from its authority.");
        return result;

        int Absolute(int register) => values[register] is { Relative: false } value ? value.Number :
            throw new InvalidDataException("Museum control uses an unresolved argument.");
        string ReadLabel(uint address)
        {
            var text = new List<byte>();
            for (int i = 0; i < 64; i++)
            {
                byte c = overlay.ReadVirtual(address + (uint)i, 1)[0];
                if (c == 0) return Encoding.ASCII.GetString(text.ToArray());
                if (c is < 32 or > 126) throw new InvalidDataException("Museum parameter label is not bounded ASCII.");
                text.Add(c);
            }
            throw new InvalidDataException("Museum parameter label is unterminated.");
        }
        void Assign(uint word)
        {
            int op = (int)(word >> 26), rs = (int)(word >> 21 & 31), rt = (int)(word >> 16 & 31);
            int imm = (short)word;
            if (op == 15) values[rt] = new((int)word << 16);
            else if (op == 9) values[rt] = values[rs] is { } value ? new(unchecked(value.Number + imm), value.Relative) : null;
            else if (op == 0 && (word & 63) is 0x21 or 0x2d)
            {
                int rd = (int)(word >> 11 & 31);
                values[rd] = rt == 0 ? values[rs] : rs == 0 ? values[rt] : null;
            }
            else if (op is 0x20 or 0x21 or 0x23 or 0x24 or 0x25) values[rt] = null;
            else if (op != 0x2b) throw new InvalidDataException("Unrecognized instruction in the Museum registration window.");
            values[0] = new(0);
        }
    }
}
