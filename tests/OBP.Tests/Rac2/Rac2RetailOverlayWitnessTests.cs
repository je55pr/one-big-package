using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.PS2.Elf;
using OBP.RAC2.Gameplay;
using OBP.RAC2.Level;
using static OBP.Tests.Rac2RetailLevelFixtures;

namespace OBP.Tests;

/// <summary>
/// Retail-gated witnesses read straight from the Going Commando boot executable and the level overlay: the
/// bolt-reward percentage tables, the packed reward-selector block, and the state-21 weapon object identity.
/// Retail-gated on <c>OBP_GC_ISO</c>.
/// </summary>
public class Rac2RetailOverlayWitnessTests
{
    [SkippableFact]
    public void AllLevels_BoltRewardPercentageTablesMatchRetailOverlay()
    {
        var iso = RequireIso();

        byte[] expected = [
            .. Rac2BoltReward.AuthoredBank0, .. Rac2BoltReward.AuthoredBank1,
            .. Rac2BoltReward.EmissionBank0, .. Rac2BoltReward.EmissionBank1
        ];

        for (int level = 0; level < 27; level++)
        {
            var (wad, header) = OpenLevelWad(iso, level);
            var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));
            Assert.Equal(expected, overlay.ReadVirtual(Rac2BoltReward.AuthoredBank0Address, expected.Length));
        }
    }

    [SkippableFact]
    public void BootRewardSelectorBaseBlockStartsZeroInRetailFileImage()
    {
        var iso = RequireIso();

        using var isoReader = new FileRandomAccessReader(iso);
        var disc = Ps2Boot.OpenDisc(isoReader);
        var programHeaders = Ps2Boot.ReadBootProgramHeaders(disc);
        var selectors = Elf32Reader.ReadVirtualRange(
            disc.BootExecutable, programHeaders,
            Rac2BoltReward.PackedSelectorBaseAddress, Rac2BoltReward.PackedSelectorTableBytes);

        Assert.Equal(Rac2BoltReward.PackedSelectorTableBytes, selectors.Length);
        Assert.All(selectors, value => Assert.Equal(0, value));
    }

    [SkippableFact]
    public void Level1_State21WeaponObjectIdentityMatchesRetail()
    {
        var iso = RequireIso();

        var (wad, header) = OpenLevelWad(iso, 1);
        var overlay = Rac2LevelOverlay.Open(Rac2LevelWad.RequireLump(wad, header, 0));

        // This transition only falls through to SetPlayerState(21, 1) when the
        // effective live weapon index at player-global +0x1248 equals 10.
        var transition = overlay.ReadVirtual(0x002C52D4, 0x1C);
        uint[] expectedTransition =
        [
            0x8C631248, // lw v1,+0x1248(v1)
            0x2402000A, // li v0,10
            0x54620006, // bnel v1,v0,...
            0x8482034E, // branch-likely delay slot
            0x24040015, // li a0,21
            0x0C0AFAE4, // jal 0x002BEB90
            0x24050001, // li a1,1
        ];
        Assert.Equal(expectedTransition, Words(transition));

        // The live weapon allocator stores its effective index beside the live
        // Moby pointer, and index 10 has an explicit special path.
        Assert.Equal(0xAE121248u, Word(overlay.ReadVirtual(0x002B072C, 4)));
        Assert.Equal(0xAE051220u, Word(overlay.ReadVirtual(0x002B0788, 4)));
        Assert.Equal(0x1642000Du, Word(overlay.ReadVirtual(0x002B07C4, 4)));

        // Retail's index map is identity at slot 10. Metadata record 10 has
        // live Moby class 71 at +0x14, the class passed to the normal allocator.
        using var isoReader = new FileRandomAccessReader(iso);
        var disc = Ps2Boot.OpenDisc(isoReader);
        var programHeaders = Ps2Boot.ReadBootProgramHeaders(disc);
        var mapEntry = Elf32Reader.ReadVirtualRange(disc.BootExecutable, programHeaders, 0x00139572, 1);
        Assert.Equal((byte)Rac2PlayerWeaponIdentity.State21EffectiveWeaponIndex, mapEntry[0]);
        Assert.Equal((uint)Rac2PlayerWeaponIdentity.State21MobyClass, Word(overlay.ReadVirtual(0x00264074, 4)));

        // State 21 checks that it is still active before calling the launch
        // helper. That helper loads +0x1220 and drives the live Moby to state 10.
        Assert.Equal(0x8E032294u, Word(overlay.ReadVirtual(0x002BB0A4, 4)));
        Assert.Equal(0x0C0AE7BCu, Word(overlay.ReadVirtual(0x002BB0B0, 4)));
        Assert.Equal(0x8E141220u, Word(overlay.ReadVirtual(0x002B9FAC, 4)));
        Assert.Equal(0x2402000Au, Word(overlay.ReadVirtual(0x002B9FD4, 4)));
        Assert.Equal(0xA2820020u, Word(overlay.ReadVirtual(0x002B9FE4, 4)));
    }

    private static uint Word(byte[] bytes)
    {
        if (bytes.Length != sizeof(uint))
        {
            throw new ArgumentException("Expected exactly one 32-bit word.", nameof(bytes));
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static uint[] Words(byte[] bytes)
    {
        if ((bytes.Length & 3) != 0)
        {
            throw new ArgumentException("Expected a whole number of 32-bit words.", nameof(bytes));
        }

        var words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)));
        }

        return words;
    }
}
