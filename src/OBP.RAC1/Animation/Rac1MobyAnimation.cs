using System.Buffers.Binary;

namespace OBP.RAC1.Animation;

/// <summary>
/// Structural R&amp;C1 Moby sequence/frame decoding. Retail NTSC-U uses the
/// RAC1/GC/UYA sequence container family, but pose evaluation remains separate
/// until the R&amp;C1 skeleton transform semantics are fully pinned.
/// </summary>
public static class Rac1MobyAnimation
{
    public const int SequenceHeaderSize = 0x1c;
    public const int FrameHeaderSize = 0x10;
    public const int SoundDefinitionSize = 0x20;
    public const byte NoSoundId = 0xff;

    public sealed record ClassSoundDefinition(int Id, int Offset);

    public sealed record ClassSoundTable(
        byte Count,
        int Offset,
        IReadOnlyList<ClassSoundDefinition> Definitions)
    {
        public bool HasDefinitions => Count != 0;
    }

    public sealed record TimedSoundCue(uint RawWord, ushort PositionUnits, ushort SoundId)
    {
        public float PositionFrames => PositionUnits / 16f;
    }

    public sealed record JointQuaternion(short X, short Y, short Z, short W)
    {
        public float Xf => X / 32768f;
        public float Yf => Y / 32768f;
        public float Zf => Z / 32768f;
        public float Wf => W / 32768f;
    }

    public sealed record Frame(
        uint TransitionRateRaw,
        ushort TimestampUnits,
        ushort DataSizeQwords,
        ushort JointDataSize,
        ushort Thing1Count,
        ushort UnknownC,
        ushort Thing2Count,
        IReadOnlyList<JointQuaternion> JointRotations,
        IReadOnlyList<ulong> Thing1,
        IReadOnlyList<ulong> Thing2)
    {
        public float TransitionRate => BitConverter.Int32BitsToSingle(unchecked((int)TransitionRateRaw));
    }

    public sealed record Sequence(
        int Index,
        float SphereX,
        float SphereY,
        float SphereZ,
        float SphereW,
        byte SoundId,
        byte TriggerCount,
        byte Unknown13,
        uint Opaque14,
        uint ConstantTransitionRateRaw,
        IReadOnlyList<uint> FrameEntries,
        IReadOnlyList<uint> TimedSoundWords,
        IReadOnlyList<TimedSoundCue> TimedSoundCues,
        IReadOnlyList<Frame> Frames)
    {
        public bool HasDirectSound => SoundId != NoSoundId;
        public float ConstantTransitionRate => BitConverter.Int32BitsToSingle(unchecked((int)ConstantTransitionRateRaw));
    }

    public sealed record SequenceSlot(int Index, Sequence? Value);

    private static int Align(int value, int amount) => ((value + amount - 1) / amount) * amount;

    public static ClassSoundTable ReadClassSoundTable(byte[] bytes)
    {
        if (bytes.Length < 0x48)
            throw new InvalidDataException("R&C1 Moby class buffer shorter than the 0x48 header.");

        byte count = bytes[0x0d];
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x28));
        if (count == 0)
            return new ClassSoundTable(count, offset, []);
        if (offset <= 0 || (long)offset + (long)count * SoundDefinitionSize > bytes.Length)
            throw new InvalidDataException("R&C1 Moby class-local sound definition table is out of range.");

        var definitions = new ClassSoundDefinition[count];
        for (int id = 0; id < count; id++)
            definitions[id] = new ClassSoundDefinition(id, offset + id * SoundDefinitionSize);
        return new ClassSoundTable(count, offset, definitions);
    }

    public static IReadOnlyList<SequenceSlot> ReadSequences(byte[] bytes)
    {
        if (bytes.Length < 0x48)
        {
            throw new InvalidDataException("R&C1 Moby class buffer shorter than the 0x48 header.");
        }
        int jointCount = bytes[0x08];
        int sequenceCount = bytes[0x0c];
        if (sequenceCount == 0)
        {
            return [];
        }
        if (0x48L + sequenceCount * 4L > bytes.Length)
        {
            throw new InvalidDataException("R&C1 Moby sequence offset list is out of range.");
        }
        return ReadSequenceSlots(
            bytes, sequenceCount, jointCount,
            index => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x48 + index * 4)),
            frameOffsetsRelativeToSequence: false,
            kind: "Moby");
    }

    /// <summary>
    /// Decode the dedicated 256-slot R&amp;C1 Ratchet sequence table from the
    /// level core. Retail NTSC-U stores frame pointers relative to each
    /// sequence block, unlike ordinary class-local Moby sequence pointers.
    /// </summary>
    public static IReadOnlyList<SequenceSlot> ReadRatchetSequences(
        byte[] assets, byte[] index, int tableOffset, int jointCount)
    {
        const int SlotCount = 256;
        if (jointCount <= 0)
            throw new InvalidDataException("R&C1 Ratchet sequence decoding requires a positive joint count.");
        if (tableOffset <= 0 || (long)tableOffset + SlotCount * 4L > index.Length)
            throw new InvalidDataException("R&C1 Ratchet sequence table is out of range.");
        return ReadSequenceSlots(
            assets, SlotCount, jointCount,
            slot => BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(tableOffset + slot * 4)),
            frameOffsetsRelativeToSequence: true,
            kind: "Ratchet");
    }

    private static IReadOnlyList<SequenceSlot> ReadSequenceSlots(
        byte[] bytes,
        int count,
        int jointCount,
        Func<int, int> sequenceOffsetAt,
        bool frameOffsetsRelativeToSequence,
        string kind)
    {
        var slots = new List<SequenceSlot>(count);
        for (int index = 0; index < count; index++)
        {
            int sequenceOffset = sequenceOffsetAt(index);
            if (sequenceOffset == 0)
            {
                slots.Add(new SequenceSlot(index, null));
                continue;
            }
            slots.Add(new SequenceSlot(index, ReadSequence(
                bytes, sequenceOffset, jointCount, index,
                frameOffsetsRelativeToSequence, kind)));
        }
        return slots;
    }
    private static Sequence ReadSequence(
        byte[] bytes,
        int sequenceOffset,
        int jointCount,
        int index,
        bool frameOffsetsRelativeToSequence,
        string kind)
    {
        if (sequenceOffset < 0 || (long)sequenceOffset + SequenceHeaderSize > bytes.Length)
            throw new InvalidDataException($"R&C1 {kind} sequence {index} offset {sequenceOffset} is out of range.");

        int frameCount = bytes[sequenceOffset + 0x10];
        byte soundId = bytes[sequenceOffset + 0x11];
        byte triggerCount = bytes[sequenceOffset + 0x12];
        byte unknown13 = bytes[sequenceOffset + 0x13];
        long frameTableEnd = (long)sequenceOffset + 0x1c + frameCount * 4L;
        long triggerListEnd = frameTableEnd + triggerCount * 4L;
        if (triggerListEnd > bytes.Length)
            throw new InvalidDataException($"R&C1 {kind} sequence {index} frame/trigger lists are out of range.");

        var frameEntries = new uint[frameCount];
        var frames = new List<Frame>(frameCount);
        for (int f = 0; f < frameCount; f++)
        {
            uint entry = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x1c + f * 4));
            frameEntries[f] = entry;
            if ((entry & 0xf0000000u) != 0)
                throw new InvalidDataException($"R&C1 {kind} sequence {index} frame {f} uses unsupported special-frame flag 0x{entry >> 28:x}.");
            int rawFrameOffset = checked((int)(entry & 0x0fffffffu));
            int frameOffset = frameOffsetsRelativeToSequence
                ? checked(sequenceOffset + rawFrameOffset)
                : rawFrameOffset;
            frames.Add(ReadFrame(bytes, frameOffset, jointCount, index, f));
        }

        var timedSoundWords = new uint[triggerCount];
        var timedSoundCues = new TimedSoundCue[triggerCount];
        for (int i = 0; i < triggerCount; i++)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)frameTableEnd + i * 4));
            timedSoundWords[i] = word;
            timedSoundCues[i] = new TimedSoundCue(
                word,
                checked((ushort)(word >> 16)),
                checked((ushort)(word & 0xffff)));
        }

        float sphereX = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset));
        float sphereY = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 4));
        float sphereZ = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 8));
        float sphereW = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 12));
        uint opaque14 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x14));
        uint constantTransitionRateRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x18));
        return new Sequence(
            index, sphereX, sphereY, sphereZ, sphereW,
            soundId, triggerCount, unknown13,
            opaque14, constantTransitionRateRaw,
            frameEntries, timedSoundWords, timedSoundCues, frames);
    }

    private static Frame ReadFrame(byte[] bytes, int frameOffset, int jointCount, int sequenceIndex, int frameIndex)
    {
        if (frameOffset <= 0 || (long)frameOffset + FrameHeaderSize > bytes.Length)
        {
            throw new InvalidDataException(
                $"R&C1 Moby sequence {sequenceIndex} frame {frameIndex} offset {frameOffset} is out of range.");
        }
        uint transitionRateRaw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(frameOffset));
        ushort timestampUnits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 4));
        ushort dataSizeQwords = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 6));
        ushort jointDataSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 8));
        ushort thing1Count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 0x0a));
        ushort unknownC = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 0x0c));
        ushort thing2Count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 0x0e));

        int expectedJointBytes = checked(jointCount * 8);
        if (jointDataSize != expectedJointBytes)
        {
            throw new InvalidDataException(
                $"R&C1 Moby sequence {sequenceIndex} frame {frameIndex} joint data size {jointDataSize} != {expectedJointBytes}.");
        }

        int payloadBytes = checked(jointDataSize + (thing1Count + thing2Count) * 8);
        int expectedBodyBytes = Align(payloadBytes, 0x10);
        if (dataSizeQwords * 0x10 != expectedBodyBytes ||
            (long)frameOffset + FrameHeaderSize + expectedBodyBytes > bytes.Length)
        {
            throw new InvalidDataException(
                $"R&C1 Moby sequence {sequenceIndex} frame {frameIndex} body size is inconsistent or out of range.");
        }

        int cursor = frameOffset + FrameHeaderSize;
        var rotations = new JointQuaternion[jointCount];
        for (int j = 0; j < jointCount; j++)
        {
            rotations[j] = new JointQuaternion(
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(cursor)),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(cursor + 2)),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(cursor + 4)),
                BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(cursor + 6)));
            cursor += 8;
        }

        var thing1 = new ulong[thing1Count];
        for (int i = 0; i < thing1.Length; i++, cursor += 8)
        {
            thing1[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(cursor));
        }
        var thing2 = new ulong[thing2Count];
        for (int i = 0; i < thing2.Length; i++, cursor += 8)
        {
            thing2[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(cursor));
        }

        return new Frame(
            transitionRateRaw, timestampUnits, dataSizeQwords, jointDataSize,
            thing1Count, unknownC, thing2Count, rotations, thing1, thing2);
    }
}
