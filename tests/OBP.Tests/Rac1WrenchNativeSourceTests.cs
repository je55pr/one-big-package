using System.Buffers.Binary;
using System.Security.Cryptography;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1WrenchNativeSourceTests
{
    [SkippableFact]
    public void RetailOverlayBindsProfilesDispatchHitProducerFlightAndAnimatedSocket()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        int Word(int address) => BinaryPrimitives.ReadInt32LittleEndian(read(address, 4));
        void Block(int start, int end, string expected) => Assert.Equal(expected,
            Convert.ToHexString(SHA256.HashData(read(start, end - start))).ToLowerInvariant());
        Block(0x225bdc, 0x2265e0, "9c9f49df7e6315b87e61244cd5d804df628358730d9d851c87b44a76e97141a5");
        Block(0x22b168, 0x22b434, "a8b3ed16b06924716a84b7ce6198ffa1e3b449c2870c8e100046bfa0ada22f00");
        Block(0x21a92c, 0x21b6c0, "cd14e1fc656698977560e3b14a07335dea5f3e3e4062baaaaf87058346f0d432");
        Block(0x228068, 0x2284a8, "dcbcf48c6361ba485ead61d0a5bccc4957a4aa8cd6441a902e31ec839886f6e1");
        Block(0x217658, 0x217958, "536b9b0eeb8c1d445cf77064eaa105bcbc1da21c1bb89bdb2b757fbb8ceeede6");
        Block(0x2a883c, 0x2a8da8, "be6fa481f7550c9be2f107a49dd173ee16bcdcb622007b4fe83c86d9d3d8b9dc");
        Block(0x2a9134, 0x2a9404, "13c6d81a43cf4d59e3380065541a403b8b453911297bb0d19de9169e4059b7ff");
        Block(0x2a82c0, 0x2a8418, "01b93399fd4b3efa56c5c3c746354877bb8b8ab2c6e8bb518ee609bfc6b3f1b9");
        Block(0x209240, 0x209298, "6c42ef3fbd000c83b1aa4de551460ae076ea26731b3fa8ce721ef5076799ce66");
        Block(0x20f6f8, 0x20f814, "98430a859b3602b912f18fe7366a9821dc8cab33c4c4c1b114d195b16e776b35");
        Block(0x253fa0, 0x254250, "89b9552b8a80a20b9d29b07872d3df03350925938a9c8a9589a013af10b02a03");
        Block(0x2325e0, 0x23266c, "2c6fb016dd3f96856a2dcbefdf4426cde71631e06391ee7293b79ec49377b526");
        Block(0x232864, 0x23293c, "59a9a7d76681309948564f5bfc85eb59b5cab7386e0d94a7db37d5eeb81405e7");
        Block(0x2d51c8, 0x2d532c, "893aebc49aeb3138c8ac3207f2c9aeb30ab3e192a21d437555d8d96f82ef4bc7");
        foreach (var profile in Rac1WrenchAttackProfile.Profiles)
        {
            int[] fields = [profile.Family, profile.ComboIndex, profile.ChainBefore, profile.BufferReference,
                profile.ChainFrom, profile.JumpFrom, profile.FinishAfter, profile.HitFrom,
                profile.HitThrough, profile.TurnFrom, profile.TurnThrough];
            for (int field = 0; field < fields.Length; field++)
                Assert.Equal(fields[field], Word(0x17bc28 + profile.Id * 44 + field * 4));
            Assert.Equal(0x225bdc, Word(0x1e8030 + profile.NativeAction * 4));
            Assert.Equal(0x22b168, Word(0x1e82b0 + profile.NativeAction * 4));
        }
        Assert.Equal(0x21b1d0, Word(0x1e7e20 + 0x13 * 4));
        Assert.Equal(0x21a92c, Word(0x1e7e20 + 0x14 * 4));
        Assert.Equal(0x21ae00, Word(0x1e7e20 + 0x15 * 4));
        Assert.Equal(71, Word(0x179ac0 + 8 * 0x4c + 0x10));
        Assert.Equal(0, Word(0x179ac0 + 8 * 0x4c + 0xc));
        // Float subtraction and store are retained even for a lethal two-point hit.
        Assert.Equal(0x46070001, Word(0x2d5288));
        Assert.Equal(unchecked((int)0xe6200020), Word(0x2d52a4));
    }
}
