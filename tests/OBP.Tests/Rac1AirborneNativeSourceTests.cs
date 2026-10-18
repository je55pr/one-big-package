using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.IO;
using OBP.RAC1.Level;
using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneNativeSourceTests
{
    [SkippableFact]
    public void CanonicalOverlayPinsAirborneProfilesSolversAndGlideInsteadOfFittedGravity()
    {
        var read = ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        float Float(int address) => BitConverter.Int32BitsToSingle(Word(address));
        void Block(int start, int end, string expected) => Assert.Equal(expected,
            Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        float Immediate(int address)
        {
            int upper = Word(address);
            Assert.Equal(0x3c01, (int)((uint)upper >> 16));
            uint bits = (uint)(upper & 0xffff) << 16;
            int next = Word(address + 4);
            if ((uint)next >> 16 == 0x3421)
            {
                bits |= (uint)(next & 0xffff);
                next = Word(address + 8);
            }
            Assert.Equal(0x44810000u, (uint)next & ~0xf800u);
            return BitConverter.Int32BitsToSingle((int)bits);
        }

        Block(0x224590, 0x225788, "78dba8d78e8ddcc35f9914f22403a11a45ab5f439f320b573a4e39da8ae9af35");
        Block(0x214108, 0x214658, "2c4e20bd3117861ae7d9068ffb1648094e5c0db29ac425c84a6433ddbfe0ca80");
        Block(0x214658, 0x214eb8, "ec10ea2ff7b985086e3d2f13de88d1879ed81fffabf1ecdde2829e6501c24aee");
        Block(0x21e8cc, 0x21ead0, "7fbb4614f43d18008d24fe40caa5bd5372ea57c27bbb8c62a382156e30f81e2d");
        Block(0x22f518, 0x22f838, "4ce62726ce93a7a183e6c4f384394a4d5e1d1adca1808c98cc4c2fc65e596414");
        Block(0x225b3c, 0x225bdc, "00fa3afb52b643dbc548d6254c6204d3fc1ae912474311e34360ca9ffe8f0ba8");
        Block(0x221df8, 0x221edc, "24162f445a07e2df06f3380be1e9d72089f7d5f00e6517cd9649ea04b1a734e3");
        Block(0x229660, 0x229910, "53c957129c23312577c51a4e54f7618fe98cbefe9472ca9b00d83f35d2400fc2");
        Block(0x224520, 0x224590, "3f1928254490c93ea3d40d50e1ba4b07974ef80e424fe83d26b8f815de0c4a3d");
        Block(0x2255a0, 0x225688, "f750ae7ca6f0ef111e47af9d56ec95c2f24cb5a896419ca3f278ab6086cc1293");
        Block(0x225acc, 0x225b18, "f5c7fc638cd1239643a0f4bd863747860595b32c78311493c1651475bfd933b4");
        Block(0x213e68, 0x214108, "407c612f2760ec01fea85e737bb543ce8bbdf0cb41bd1e3df314c80069663437");
        Block(0x213de8, 0x213e68, "86886dce42cc639c6bd481db17cf02246fe1a59f300614b786bf1617aa1891b0");
        Block(0x223e5c, 0x223ebc, "6a24f0564e962e9a8a6001624b99850b271b4e8d0a36a06def0e102c82ef7841");
        Block(0x21a720, 0x21a92c, "18da7b40e6da6ea1d2ed35575c569425dbb0aa47b5bacc91fe82be81933a0912");
        Block(0x22c8e8, 0x22ca24, "d4f1fa9f728af01e732d3b92ddc5a06b47c44593e6d3a6ea2bbd6dddece70ffa");
        Block(0x22eae4, 0x22ebcc, "ceaeac55d4090620db19c7cd4381f19aaef070c6322749c52f0ae74184fa0e98");
        Block(0x20b5b8, 0x20b928, "a7cf37e6ac406c1a937f6ea1da7a6f682bbad699e135791344ba61eb4142c58a");
        Block(0x22f414, 0x22f4d4, "804352322a6009b462c17d7ec72ad3d68ab100c2fb9f0185c205759ecdee8581");
        Block(0x21ed70, 0x21ef18, "a28ab6bc964e804288d176fcd5a46e4a4a0b16a4c56775ead0cce7e726154178");
        Block(0x22ee30, 0x22ef44, "7a1d30f983026c652dc6d2c5871bb88885ee4008194469b07222e5e253f528d8");
        Block(0x22f224, 0x22f388, "230d3fe03da8a4387017566a690b1dd5c73928d5911a63da8ffd6d9fe54881d0");
        Block(0x2293a8, 0x229660, "29c9a9792e1eef191307bd0dc1e72385c1346f49ccccb1fc86f9e804b326334d");
        Block(0x216710, 0x2167d0, "da0c0c3516a42fe7f53e6bdbf64ffea4ddb2fbcd4f145bff0fd6361d29c495cc");
        Block(0x22371c, 0x223840, "f260cec6aff561675b4aaaf7c08f2d9d7caa5b1c27cd26c2da81ac0d813dbb1b");
        Block(0x25f730, 0x25f77c, "923f595e16a3eca3f2c38c1d8c7e45c9f6028db9a7b4a343f17b5bf39e8b13f8");
        Block(0x222610, 0x2226d8, "aeb5e1b56eaf2b1b7d6f6a16a82ce66e2474311564259711b188c5f270b85a96");
        Block(0x217e0c, 0x217fcc, "1bb0e0930f3f72f72adba9153966cfa295da3f15e7efd29e342c1225496e2d69");
        Assert.Equal(0x21ead0, Word(0x1e7e20 + 7 * 4));
        Assert.Equal(0x21e8cc, Word(0x1e7e20 + 8 * 4));
        Assert.Equal(0x224520, Word(0x1e8030 + 8 * 4));
        Assert.Equal(0x224590, Word(0x1e8030 + 0xb * 4));
        Assert.Equal(0x21a720, Word(0x1e7e20 + Rac1AirbornePowerSlamSession.NativeAction * 4));
        // Entry/update are jump-table labels within their shared dispatcher.
        Assert.Equal(0x223e5c, Word(0x1e8030 + Rac1AirbornePowerSlamSession.NativeAction * 4));
        Assert.Equal(0x22c8e8, Word(0x1e82b0 + Rac1AirbornePowerSlamSession.NativeAction * 4));
        Assert.Equal(unchecked((int)0xae400428), Word(0x2246c8));
        Assert.Equal(unchecked((int)0xae40042c), Word(0x2246cc));
        Assert.Equal(0x4a0103bd, Word(0x1ff1e8)); // VU VSQRT, not a fitted impulse table.

        foreach (var sample in new[]
        {
            (Rac1AirborneJumpKind.Stationary, 0x224750, 0x224790),
            (Rac1AirborneJumpKind.Moving, 0x224d10, 0x224d44),
            (Rac1AirborneJumpKind.HeliPackLong, 0x224f28, 0x224f60),
            (Rac1AirborneJumpKind.DirectionalFlip, 0x225690, 0x2256bc),
            (Rac1AirborneJumpKind.ThrusterPackHigh, 0x225340, 0x225378),
            (Rac1AirborneJumpKind.HeliPackHigh, 0x22546c, 0x22549c),
            (Rac1AirborneJumpKind.LedgeJump, 0x225144, 0x225180),
            (Rac1AirborneJumpKind.WallJump, 0x225280, 0x2252c0),
        })
        {
            var profile = Rac1AirborneJumpProfile.Create(sample.Item1);
            Assert.Equal(profile.MinimumHeight, Immediate(sample.Item2));
            Assert.Equal(profile.MaximumHeight, Immediate(sample.Item3));
        }
        Assert.Equal(29.7f, Immediate(0x22466c));
        Assert.Equal(11f, Immediate(0x224684)); // Common f21 used by Heli long jump.
        Assert.Equal(8.5f, Immediate(0x225108));
        Assert.Equal(25f, Immediate(0x2251b8));
        Assert.Equal(3f, Immediate(0x21e9b4));
        Assert.Equal(5f, Immediate(0x21e9cc));
        Assert.Equal(0.72f, Immediate(0x21ea80));
        Assert.Equal(15f, Immediate(0x21ea3c));
        Assert.Equal(7f, Immediate(0x21ea44));
        Assert.Equal(4.5f, Immediate(0x22452c));
        Assert.Equal(7f, Immediate(0x214148));
        Assert.Equal(150f, Immediate(0x2142a8));
        Assert.Equal(Rac1OrdinaryGroundContactMotion.JumpAnticipationDownwardRequestPerTick,
            (double)(Immediate(0x214594) * Rac1AirborneJumpProfile.SquaredSecondsPerUpdate));
        Assert.Equal(0.8f, Immediate(0x225afc));
        Assert.Equal(3.5f, Immediate(0x225658));
        Assert.Equal(11.5f, Immediate(0x2250b8));
        Assert.Equal(1.9f, Immediate(0x2253cc));
        Assert.Equal(3.3f, Immediate(0x2254fc));
        Assert.Equal(29f, Immediate(0x225304));
        Assert.Equal(100f, Immediate(0x223e68));
        Assert.Equal(30f, Immediate(0x21a720));
        Assert.Equal(5.7f, Immediate(0x21a778));
        Assert.Equal(70f, Immediate(0x21a784));
        Assert.Equal(Rac1AirborneWallProbe.MinimumWallAngle, Immediate(0x20b7c4));
        Assert.Equal(Rac1AirborneWallProbe.MaximumFacingError, Immediate(0x20b814));
        Assert.Equal(Rac1AirborneWallProbe.MinimumRepeatedNormalDifference, Immediate(0x20b8d0));
        Assert.Equal(53f * (1f / 3600f), Immediate(0x21eefc) * (1f / 3600f));
        Assert.Equal(Rac1AirborneLandingSession.LaterLandingRequestScale, Immediate(0x21edc0));
        Assert.Equal(Rac1AirborneLandingSession.HeliStopCarryScale, Immediate(0x22f268));
        Assert.Equal(Rac1AirborneLandingSession.StopCarryLimit, Immediate(0x229584) * (1f / 60f));
        Assert.Equal(Rac1AirborneHeliLongSession.MaximumTargetStep, Immediate(0x2246ec) * (1f / 60f));
        // The 2/60 write belongs to action0x49's branch; action0xa begins only
        // at224f28 and jumps to225678 without overriding the common P+0x424.
        Assert.Equal(0x2402000a, Word(0x224e78));
        Assert.Equal(0x5602002b, Word(0x224f20));
        Assert.Equal(unchecked((int)0x7ca20000), Word(0x222668)); // idle copies contact P110 into carry P150.
        Assert.Equal(unchecked((int)0x7ca20000), Word(0x223738)); // stop owns the same contact-copy entry.
        Assert.Equal(Rac1AirborneLandingSession.IdleCarryDeceleration, Immediate(0x217f8c) * (1f / 3600f));
        Assert.Equal(Rac1AirborneLandingSession.StopCarryDeceleration, Immediate(0x217e44) * (1f / 3600f));
        // Flip motion owns P194 independently from the directional request.
        Assert.Equal(0x50620003, Word(0x2149b4)); // back direction3 branches through zeroing P194.
        Assert.Equal(unchecked((int)0xae000194), Word(0x2149b8));
        Assert.Equal(unchecked((int)0xc6000454), Word(0x2149bc)); // other directions read retained P454.
        Assert.Equal(unchecked((int)0xe6000194), Word(0x2149c0));
        Assert.Equal(0x0062182a, Word(0x2149f0)); // predictedLanding < scaled12, not greater than12.
        Assert.Equal(0x10600009, Word(0x2149f4));
        Assert.Equal(0x0c08007c, Word(0x214a84)); // JAL absolute angular distance2001f0.
        Assert.Equal(0x0c07fe00, Word(0x214a8c)); // JAL SIN1ff800, not COS1ff7e8.
        Assert.Equal(unchecked((int)0xc6140454), Word(0x214a94)); // projection still reads P454 for back.

        Block(0x17bf20, 0x17bf5c, "91b2c4aecb0ca07b567eca907fc25d3f39a068e3a0302c06deb1e18f58c4936f");
        Block(0x15f6e8, 0x15f6f4, "539bde5fb056c95cb8b44294521414f806d4dbf62fd6b3d3a72005526977986b");
        Assert.Equal(151f, Float(0x17bf20));
        Assert.Equal(5, Word(0x17bf28));
        Assert.Equal(27, Word(0x17bf34));
        Assert.Equal(48f, Float(0x15f6e8));
        Assert.Equal(1000, Word(0x15f6f0));
    }

    internal static Func<int, int, byte[]> ReadOverlay()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");
        using var reader = new FileRandomAccessReader(iso!);
        var level = Rac1DiscIndex.Read(reader).Levels.Single(l => l.LevelId == 0);
        var outer = level.CoreRanges[0];
        var data = new SubRangeReader(reader, outer.OffsetBytes, outer.SizeBytes, "level data");
        var range = Rac1LevelData.OpenRange(data, Rac1LevelData.ReadDirectory(data), 0);
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
        Assert.Equal(7, sections.Count);
        return (address, size) =>
        {
            var section = sections.Single(s => address >= s.Address && address + size <= s.Address + s.Size);
            return payload.AsSpan(section.Offset + address - section.Address, size).ToArray();
        };
    }
}
