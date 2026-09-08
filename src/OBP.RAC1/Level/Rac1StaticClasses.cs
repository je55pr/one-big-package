using System.Buffers.Binary;
using OBP.PS2.Geometry;
using OBP.RAC1.Geometry;

namespace OBP.RAC1.Level;

/// <summary>Retail R&amp;C1 Moby/Tie/Shrub class directories over evidence-backed inner codecs.</summary>
public static class Rac1StaticClasses
{
    public const int MobyTableField = 0x18;
    public const int TieTableField = 0x20;
    public const int ShrubTableField = 0x28;
    public const int MobyEntrySize = 0x20;
    public const int TieEntrySize = 0x20;
    public const int ShrubEntrySize = 0x30;

    public sealed record MobyClass(
        int OClass,
        int AssetOffset,
        Rac1Moby.Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds,
        int JointCount);

    public sealed record TieClass(
        int OClass,
        int AssetOffset,
        RcTie.Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds);

    public sealed record ShrubClass(
        int OClass,
        int AssetOffset,
        RcShrub.Mesh Mesh,
        int[] TriangleTextureIds,
        IReadOnlyList<int> TextureIds);

    public sealed record Classes(
        IReadOnlyDictionary<int, MobyClass> Mobies,
        IReadOnlyDictionary<int, TieClass> Ties,
        IReadOnlyDictionary<int, ShrubClass> Shrubs);

    private sealed record Entry(int OClass, int AssetOffset, byte[] TextureIds);

    public static Classes Read(Rac1LevelCore.Core core)
    {
        var moby = ReadEntries(core, "moby", MobyTableField, MobyEntrySize);
        var tieEntries = ReadEntries(core, "tie", TieTableField, TieEntrySize);
        var shrubEntries = ReadEntries(core, "shrub", ShrubTableField, ShrubEntrySize);

        var boundaries = moby.Concat(tieEntries).Concat(shrubEntries)
            .Select(e => e.AssetOffset)
            .Where(o => o > 0)
            .Distinct()
            .OrderBy(o => o)
            .ToArray();
        ReadOnlySpan<byte> Payload(Entry entry)
        {
            if (entry.AssetOffset <= 0 || entry.AssetOffset >= core.Assets.Length)
            {
                throw new InvalidDataException($"R&C1 class {entry.OClass} has invalid asset offset {entry.AssetOffset}.");
            }
            int end = boundaries.FirstOrDefault(o => o > entry.AssetOffset, core.Assets.Length);
            if (end <= entry.AssetOffset)
            {
                throw new InvalidDataException($"R&C1 class {entry.OClass} has no positive asset range.");
            }
            return core.Assets.AsSpan(entry.AssetOffset, end - entry.AssetOffset);
        }

        var mobies = new Dictionary<int, MobyClass>();
        foreach (var entry in moby)
        {
            if (entry.AssetOffset == 0) continue;
            if (mobies.ContainsKey(entry.OClass))
                throw new InvalidDataException($"Duplicate R&C1 moby class id {entry.OClass}.");
            var payload = Payload(entry).ToArray();
            var mesh = Rac1Moby.ReadClass(payload);
            var mapped = MapTextureSlots("moby", entry, mesh.TriangleMaterialSlots, allowUntextured: true);
            mobies.Add(entry.OClass, new MobyClass(
                entry.OClass, entry.AssetOffset, mesh, mapped,
                mapped.Where(v => v >= 0).Distinct().OrderBy(v => v).ToArray(), mesh.JointCount));
        }

        var ties = new Dictionary<int, TieClass>();
        foreach (var entry in tieEntries)
        {
            if (entry.AssetOffset == 0)
            {
                continue;
            }
            if (ties.ContainsKey(entry.OClass))
            {
                throw new InvalidDataException($"Duplicate R&C1 tie class id {entry.OClass}.");
            }

            var mesh = RcTie.ReadClass(Payload(entry), RcTie.Rac1Layout);
            var mapped = MapTextureSlots("tie", entry, mesh.TriangleMaterialSlots);
            ties.Add(entry.OClass, new TieClass(
                entry.OClass,
                entry.AssetOffset,
                mesh,
                mapped,
                mapped.Distinct().OrderBy(v => v).ToArray()));
        }

        var shrubs = new Dictionary<int, ShrubClass>();
        foreach (var entry in shrubEntries)
        {
            if (entry.AssetOffset == 0)
            {
                continue;
            }
            if (shrubs.ContainsKey(entry.OClass))
            {
                throw new InvalidDataException($"Duplicate R&C1 shrub class id {entry.OClass}.");
            }

            var payload = Payload(entry);
            if (payload.Length < RcShrub.ClassHeaderSize)
            {
                throw new InvalidDataException($"R&C1 shrub class {entry.OClass} is shorter than its header.");
            }
            int payloadClass = BinaryPrimitives.ReadInt16LittleEndian(payload[0x24..]);
            if (payloadClass != entry.OClass)
            {
                throw new InvalidDataException($"R&C1 shrub table class {entry.OClass} disagrees with payload class {payloadClass}.");
            }

            var mesh = RcShrub.ReadClass(payload);
            var mapped = MapTextureSlots("shrub", entry, mesh.TriangleMaterialSlots);
            shrubs.Add(entry.OClass, new ShrubClass(
                entry.OClass,
                entry.AssetOffset,
                mesh,
                mapped,
                mapped.Distinct().OrderBy(v => v).ToArray()));
        }

        return new Classes(mobies, ties, shrubs);
    }
    private static List<Entry> ReadEntries(
        Rac1LevelCore.Core core,
        string family,
        int fieldOffset,
        int entrySize)
    {
        if (fieldOffset < 0 || fieldOffset + 8 > core.Index.Length)
        {
            throw new InvalidDataException($"R&C1 {family} class table field is outside the core index.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(fieldOffset));
        int offset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(fieldOffset + 4));
        if (count < 0 || count > 100_000 || offset < 0 ||
            (long)offset + (long)count * entrySize > core.Index.Length)
        {
            throw new InvalidDataException($"R&C1 {family} class table {count}@{offset} is invalid.");
        }

        var outp = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            int at = offset + i * entrySize;
            int assetOffset = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at));
            int oClass = BinaryPrimitives.ReadInt32LittleEndian(core.Index.AsSpan(at + 4));
            if (assetOffset < 0 || assetOffset > core.Assets.Length)
            {
                throw new InvalidDataException($"R&C1 {family} class {oClass} asset offset {assetOffset} is invalid.");
            }
            outp.Add(new Entry(oClass, assetOffset, core.Index.AsSpan(at + 0x10, 16).ToArray()));
        }
        return outp;
    }

    private static int[] MapTextureSlots(string family, Entry entry, int[] slots, bool allowUntextured = false)
    {
        var mapped = new int[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            int slot = slots[i];
            if (slot == -1 && allowUntextured)
            {
                mapped[i] = -1;
                continue;
            }
            if (slot < 0 || slot >= entry.TextureIds.Length)
            {
                throw new InvalidDataException($"R&C1 {family} class {entry.OClass} uses texture slot {slot} outside 0..15.");
            }
            mapped[i] = entry.TextureIds[slot];
        }
        return mapped;
    }
}
