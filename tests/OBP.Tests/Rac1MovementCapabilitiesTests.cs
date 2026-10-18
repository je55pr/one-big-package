using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1MovementCapabilitiesTests
{
    [Fact]
    public void OpeningInventoryDoesNotSilentlyAcquireMovementEquipment()
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        var capabilities = Rac1MovementCapabilities.FromInventory(inventory);
        Assert.Equal(default, capabilities);
        Assert.Null(capabilities.RememberedBackpack(inventory));
        Assert.Equal(8, inventory.CurrentItemId);
    }

    [Theory]
    [InlineData(Rac1TraversalEquipmentId.HeliPack)]
    [InlineData(Rac1TraversalEquipmentId.ThrusterPack)]
    [InlineData(Rac1TraversalEquipmentId.HydroPack)]
    [InlineData(Rac1TraversalEquipmentId.OxygenMask)]
    [InlineData(Rac1TraversalEquipmentId.Swingshot)]
    [InlineData(Rac1TraversalEquipmentId.Magneboots)]
    [InlineData(Rac1TraversalEquipmentId.Grindboots)]
    public void AcquisitionAdmitsOnlyTheCorrespondingCapability(Rac1TraversalEquipmentId equipment)
    {
        var inventory = Rac1WeaponInventory.CreateOpeningVeldinWitness();
        inventory.AcquireNativeItem((int)equipment);
        var capabilities = Rac1MovementCapabilities.FromInventory(inventory);
        foreach (var candidate in Enum.GetValues<Rac1TraversalEquipmentId>())
            Assert.Equal(candidate == equipment, capabilities.Owns(candidate));
        Assert.Equal(8, inventory.CurrentItemId);
    }

    [Fact]
    public void RememberedBackpackRequiresBothOwnershipAndTheNativeSaveSlot()
    {
        var items = new byte[37];
        items[2] = 1;
        items[3] = 1;
        var gadgets = new int[7];
        gadgets[3] = 2;
        var state = new Rac1WeaponPersistentState(new int[37], items, new byte[37], new int[8], 0, gadgets);
        var inventory = new Rac1WeaponInventory(state);
        var capabilities = Rac1MovementCapabilities.FromInventory(inventory);
        Assert.True(capabilities.HeliPack);
        Assert.True(capabilities.ThrusterPack);
        Assert.Equal(Rac1TraversalEquipmentId.HeliPack, capabilities.RememberedBackpack(inventory));
        gadgets[3] = 4;
        inventory = new Rac1WeaponInventory(state);
        Assert.Null(capabilities.RememberedBackpack(inventory));
    }

    [Theory]
    [InlineData(Rac1TraversalEquipmentId.HeliPack, 0x4e6e, 3, 5, 0x25f)]
    [InlineData(Rac1TraversalEquipmentId.ThrusterPack, 0x4e6d, 3, 5, 0x260)]
    [InlineData(Rac1TraversalEquipmentId.HydroPack, 0x4e6f, 3, 5, 0x261)]
    [InlineData(Rac1TraversalEquipmentId.OxygenMask, 0x4e6b, 2, 4, 0x509)]
    [InlineData(Rac1TraversalEquipmentId.Swingshot, 0x4e61, 0, 1, 0xd0)]
    [InlineData(Rac1TraversalEquipmentId.Magneboots, 0x4e67, 1, 2, 0xad)]
    [InlineData(Rac1TraversalEquipmentId.Grindboots, 0x4e68, 1, 2, 0xc3)]
    public void DescriptorUsesTheNativeEquipmentNamespace(Rac1TraversalEquipmentId id,
        int name, int category, int equipmentDomain, int nativeClass)
    {
        var descriptor = Rac1MovementCapabilities.Descriptor(id);
        Assert.Equal(name, descriptor.NameTextId);
        Assert.Equal(category, descriptor.Category);
        Assert.Equal(equipmentDomain, descriptor.EquipmentDomain);
        Assert.Equal(nativeClass, descriptor.PrimaryMobyClass);
    }

    [SkippableFact]
    public void CanonicalRetailPinsEquipmentNamesSelectorsAndTetherEquations()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var directory = Rac1LevelData.ReadDirectory(data);
        var range = Rac1LevelData.OpenRange(data, directory, 0);
        byte[] payload = range.Read(0, checked((int)range.Length));
        Assert.Equal("eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804",
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());
        var sections = new List<(int Address, int Offset, int Size)>();
        for (int offset = 0; offset < payload.Length;)
        {
            int address = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            int size = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4));
            Assert.Equal(0x245c28, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 12)));
            sections.Add((address, offset + 16, size));
            offset += 16 + size;
        }
        byte[] Read(int address, int size)
        {
            var section = sections.Single(s => address >= s.Address && address + size <= s.Address + s.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        }
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(Read(address, 4));

        foreach (var id in Enum.GetValues<Rac1TraversalEquipmentId>())
        {
            int address = 0x179ac0 + (int)id * 0x4c;
            var descriptor = Rac1MovementCapabilities.Descriptor(id);
            Assert.Equal(descriptor.NameTextId, Word(address));
            Assert.Equal(descriptor.Category, Word(address + 8));
            Assert.Equal(descriptor.EquipmentDomain, Word(address + 12));
            Assert.Equal(descriptor.PrimaryMobyClass, Word(address + 16));
        }
        const int englishTextBase = 0x2381820;
        byte[] textHeader = reader.Read(englishTextBase, 8);
        int textCount = BinaryPrimitives.ReadInt32LittleEndian(textHeader);
        Assert.Equal(1762, textCount);
        byte[] textTable = reader.Read(englishTextBase + 8, textCount * 16);
        foreach (var (id, expected) in new[]
        {
            (Rac1TraversalEquipmentId.HeliPack, "Heli-Pack"), (Rac1TraversalEquipmentId.ThrusterPack, "Thruster-Pack"),
            (Rac1TraversalEquipmentId.HydroPack, "Hydro-Pack"), (Rac1TraversalEquipmentId.OxygenMask, "O2 Mask"),
            (Rac1TraversalEquipmentId.Swingshot, "Swingshot"), (Rac1TraversalEquipmentId.Magneboots, "Magneboots"),
            (Rac1TraversalEquipmentId.Grindboots, "Grindboots"),
        })
        {
            int textId = Rac1MovementCapabilities.Descriptor(id).NameTextId;
            int row = Enumerable.Range(0, textCount).Single(i =>
                BinaryPrimitives.ReadInt32LittleEndian(textTable.AsSpan(i * 16 + 4)) == textId);
            int relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(textTable.AsSpan(row * 16));
            Assert.Equal(expected, Encoding.ASCII.GetString(reader.Read(englishTextBase + relativeOffset, expected.Length)));
        }
        foreach (var (start, end, hash) in new[]
        {
            (0x179ac0, 0x17a5bc, "7de137db5bf311f577183b6704cc8a7d0a4e9b2c57a2a381609954ddfe7a22cd"),
            (0x210074, 0x210354, "291975e0c77a579e2d55c25966450e1ef28da020fdb5da05cdab4bce37596796"),
            (0x222158, 0x2223f8, "0ef8bea634276462848f742963231d180633467674582c897799d0414f152afd"),
            (0x219d74, 0x219e70, "9c26789350708743c42f398d943cfc3322894aebf416c1a59b8de6406aa04ce2"),
            (0x227778, 0x227998, "1a1c975bb0c98f05d2756abc098113021ac767f4b52bd51eeca026154680a71c"),
            (0x2c68e0, 0x2c690c, "a817d98d4c987106088f161b14bc7669866768efb588acb894f6ba94c9222457"),
        })
            Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(Read(start, end - start))).ToLowerInvariant());
        Assert.Equal(0x240202f6, Word(0x2221d0));
        Assert.Equal(0x24040026, Word(0x2c68e8));
        Assert.Equal(0x24040025, Word(0x2c68fc));
    }
}
