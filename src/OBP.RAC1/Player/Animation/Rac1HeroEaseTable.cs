using System.Buffers.Binary;
using OBP.RAC1.Level;

namespace OBP.RAC1.Player;

/// <summary>
/// Ratchet's procedural joint-easing records, read from the original level
/// overlay. The native table at <c>0x17a680</c> holds 31 records of 0xb0 bytes.
/// The first <see cref="BodyRecordCount"/> belong to the player body; the rest
/// drive other mobys (gadgets, vehicles) and are not admitted here. Each body
/// record names an animation attachment chain (resolved by the avatar skeleton
/// to a joint) and its initial spring acceleration and damping.
/// </summary>
public sealed class Rac1HeroEaseTable
{
    public const int NativeAddress = 0x17a680;
    public const int RecordStride = 0xb0;
    public const int NativeRecordCount = 31;
    public const int BodyRecordCount = 18;

    public readonly record struct Record(int AttachmentId, float Acceleration, float MaxSpeed);

    private Rac1HeroEaseTable(IReadOnlyList<Record> body) => Body = body;

    /// <summary>Body records in native ease-index order.</summary>
    public IReadOnlyList<Record> Body { get; }

    public static Rac1HeroEaseTable Create(IReadOnlyList<Record> body)
    {
        if (body.Count != BodyRecordCount)
            throw new ArgumentException($"Expected {BodyRecordCount} body ease records.", nameof(body));
        foreach (var record in body)
            if (record.AttachmentId < 0 || !float.IsFinite(record.Acceleration) || !float.IsFinite(record.MaxSpeed))
                throw new ArgumentException("Invalid ease record.", nameof(body));
        return new(body.ToArray());
    }

    public static Rac1HeroEaseTable Read(Rac1ExecutableOverlay overlay)
    {
        var body = new Record[BodyRecordCount];
        for (int i = 0; i < NativeRecordCount; i++)
        {
            var bytes = overlay.Read(NativeAddress + i * RecordStride, RecordStride);
            short attachment = BinaryPrimitives.ReadInt16LittleEndian(bytes[0xa0..]);
            short kind = BinaryPrimitives.ReadInt16LittleEndian(bytes[0xa2..]);
            // Kind 0 is the hero body moby; the table is rejected if the layout
            // does not match instead of silently easing the wrong joints.
            if ((i < BodyRecordCount) != (kind == 0))
                throw new InvalidDataException("R&C1 hero ease table does not match the recovered layout.");
            if (i >= BodyRecordCount) continue;
            body[i] = new(attachment, BitConverter.ToSingle(bytes[0xa4..]), BitConverter.ToSingle(bytes[0xa8..]));
        }
        return Create(body);
    }
}
