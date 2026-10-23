using System.Buffers.Binary;
using OBP.RAC1.Level;

namespace OBP.RAC1.Progression;

/// <summary>The compiled 0x1c-byte talk records, not a general mission/script VM.</summary>
public sealed record Rac1TalkRecord(
    int Index, int TextId, short Scene, short Next, short Condition, short Argument,
    short WhenTrue, short WhenFalse, ushort Flags)
{
    public bool Automatic => (Flags & 1) != 0;
    public bool ChainNextScene => (Flags & 4) != 0;
    public bool PayConditionCost => (Flags & 8) != 0;
}

public sealed record Rac1TalkProgram(
    int NativeLevelId, int GlobalTalkerIndex,
    IReadOnlyDictionary<int, Rac1TalkRecord> Records,
    IReadOnlyDictionary<int, int> BoltPrices)
{
    /// <summary>
    /// 0x242c1c/0x24a798 registers authored +0x74 in the current level's talker
    /// range. 0x2668a0 then selects its program from the compiled pointer table.
    /// The two admitted overlays have independently verified address bindings.
    /// </summary>
    public static Rac1TalkProgram? Read(Rac1ExecutableOverlay overlay, int nativeLevelId, Rac1Instances.MobyInstance actor)
    {
        if (nativeLevelId is not (0 or 1)) return null;
        string expected = nativeLevelId == 0
            ? "eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804"
            : "81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae";
        if (overlay.Sha256 != expected) throw new InvalidDataException("Unsupported R&C1 mission executable.");
        if (actor.RawRecord.Length != Rac1Instances.MobyRecordSize)
            throw new InvalidDataException("Talk actor lacks its original authored Moby record.");
        int local = BinaryPrimitives.ReadInt32LittleEndian(actor.RawRecord.AsSpan(0x74));
        if (local == -1) return null;
        int relocation = nativeLevelId == 0 ? 0 : 0x480;
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(overlay.Read(address, 4));
        int first = Word(0x1c44b8 + relocation + nativeLevelId * 4);
        int end = Word(0x1c44b8 + relocation + (nativeLevelId + 1) * 4);
        if (first < 0 || end < first || end > 256 || local < 0 || local >= end - first)
            throw new InvalidDataException("Talk actor has an out-of-range native talker slot.");
        int global = first + local;
        int program = Word(0x1b1678 + relocation + global * 4);
        if (program == 0) return null;
        var records = new Dictionary<int, Rac1TalkRecord>();
        var prices = new Dictionary<int, int>();
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.TryDequeue(out int index))
        {
            if (index == -1 || records.ContainsKey(index)) continue;
            if (index is < 0 or >= 128) throw new InvalidDataException("Talk program exceeds its bounded record graph.");
            var bytes = overlay.Read(checked(program + index * 0x1c), 0x1c);
            short S(int offset) => BinaryPrimitives.ReadInt16LittleEndian(overlay.Read(program + index * 0x1c + offset, 2));
            var record = new Rac1TalkRecord(index, BinaryPrimitives.ReadInt32LittleEndian(bytes),
                S(4), S(6), S(8), S(10), S(12), S(14), unchecked((ushort)S(16)));
            records.Add(index, record);
            pending.Enqueue(record.Next);
            if (record.Condition != 0)
            {
                pending.Enqueue(record.WhenTrue);
                pending.Enqueue(record.WhenFalse);
            }
            if (record.Condition == 1)
            {
                if (record.Argument is < 0 or >= 128) throw new InvalidDataException("Talk bolt-price index is out of range.");
                int cost = Word(0x1c40b0 + relocation + record.Argument * 0x18);
                if (cost < 0) throw new InvalidDataException("Negative native conversation price.");
                prices[record.Argument] = cost;
            }
        }
        return new(nativeLevelId, global, records, prices);
    }
}
