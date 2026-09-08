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

    public sealed record JointQuaternion(short X, short Y, short Z, short W)
    {
        public float Xf => X / 32768f;
        public float Yf => Y / 32768f;
        public float Zf => Z / 32768f;
        public float Wf => W / 32768f;
    }

    public sealed record Frame(
        uint Unknown0Raw,
        ushort Unknown4,
        ushort DataSizeQwords,
        ushort JointDataSize,
        ushort Thing1Count,
        ushort UnknownC,
        ushort Thing2Count,
        IReadOnlyList<JointQuaternion> JointRotations,
        IReadOnlyList<ulong> Thing1,
        IReadOnlyList<ulong> Thing2);

    public sealed record Sequence(
        int Index,
        float SphereX,
        float SphereY,
        float SphereZ,
        float SphereW,
        byte SoundCount,
        byte TriggerCount,
        byte Unknown13,
        uint TriggerDataOffset,
        uint AnimationInfo,
        IReadOnlyList<uint> FrameEntries,
        IReadOnlyList<uint> Triggers,
        IReadOnlyList<Frame> Frames);

    public sealed record SequenceSlot(int Index, Sequence? Value);

    private static int Align(int value, int amount) => ((value + amount - 1) / amount) * amount;

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

        var slots = new List<SequenceSlot>(sequenceCount);
        for (int index = 0; index < sequenceCount; index++)
        {
            int sequenceOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x48 + index * 4));
            if (sequenceOffset == 0)
            {
                slots.Add(new SequenceSlot(index, null));
                continue;
            }
            if (sequenceOffset < 0 || (long)sequenceOffset + SequenceHeaderSize > bytes.Length)
            {
                throw new InvalidDataException($"R&C1 Moby sequence {index} offset {sequenceOffset} is out of range.");
            }

            int frameCount = bytes[sequenceOffset + 0x10];
            byte soundCount = bytes[sequenceOffset + 0x11];
            byte triggerCount = bytes[sequenceOffset + 0x12];
            byte unknown13 = bytes[sequenceOffset + 0x13];
            long frameTableEnd = (long)sequenceOffset + 0x1c + frameCount * 4L;
            long triggerListEnd = frameTableEnd + triggerCount * 4L;
            if (triggerListEnd > bytes.Length)
            {
                throw new InvalidDataException($"R&C1 Moby sequence {index} frame/trigger lists are out of range.");
            }

            var frameEntries = new uint[frameCount];
            var frames = new List<Frame>(frameCount);
            for (int f = 0; f < frameCount; f++)
            {
                uint entry = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x1c + f * 4));
                frameEntries[f] = entry;
                if ((entry & 0xf0000000u) != 0)
                {
                    throw new InvalidDataException($"R&C1 Moby sequence {index} frame {f} uses an unsupported special-frame flag 0x{entry >> 28:x}.");
                }
                int frameOffset = checked((int)(entry & 0x0fffffffu));
                frames.Add(ReadFrame(bytes, frameOffset, jointCount, index, f));
            }

            var triggers = new uint[triggerCount];
            for (int i = 0; i < triggerCount; i++)
            {
                triggers[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)frameTableEnd + i * 4));
            }

            float sphereX = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset));
            float sphereY = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 4));
            float sphereZ = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 8));
            float sphereW = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(sequenceOffset + 12));
            uint triggerDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x14));
            uint animationInfo = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(sequenceOffset + 0x18));
            slots.Add(new SequenceSlot(index, new Sequence(
                index, sphereX, sphereY, sphereZ, sphereW,
                soundCount, triggerCount, unknown13,
                triggerDataOffset, animationInfo,
                frameEntries, triggers, frames)));
        }
        return slots;
    }

    private static Frame ReadFrame(byte[] bytes, int frameOffset, int jointCount, int sequenceIndex, int frameIndex)
    {
        if (frameOffset <= 0 || (long)frameOffset + FrameHeaderSize > bytes.Length)
        {
            throw new InvalidDataException(
                $"R&C1 Moby sequence {sequenceIndex} frame {frameIndex} offset {frameOffset} is out of range.");
        }
        uint unknown0 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(frameOffset));
        ushort unknown4 = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(frameOffset + 4));
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
            unknown0, unknown4, dataSizeQwords, jointDataSize,
            thing1Count, unknownC, thing2Count, rotations, thing1, thing2);
    }
}
