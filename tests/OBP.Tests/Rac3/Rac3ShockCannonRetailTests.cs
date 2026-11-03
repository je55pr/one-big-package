using OBP.Core;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using OBP.IO;
using OBP.PS2.Compression;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.RAC3.Level;

namespace OBP.Tests;

public sealed partial class Rac3ShockCannonRetailTests
{
    [SkippableFact]
    public void OriginalDiscNamesDefinitionsUpgradeLinksAndAmmoIdentifyV5Cannon()
    {
        using FileRandomAccessReader disc = OpenAuthority();
        byte[] overlay = ReadTable1Overlay(disc);
        byte[] text = ReadEnglishText(disc);
        foreach (Rac3ShockCannonUpgrade item in Rac3ShockCannonProfile.UpgradeFamily)
        {
            byte[] row = Virtual(overlay, (uint)(Rac3ShockCannonProfile.ItemTableAddress + item.DefinitionIndex * 0xf0), 0xf0);
            Assert.Equal(1, row[0]);
            Assert.Equal(item.NativeLevel, row[1]);
            Assert.Equal(11, U16(row, 2));
            Assert.Equal(item.NameTextId, I32(row, 4));
            Assert.Equal(item.Name, TextForId(text, item.NameTextId));
            Assert.Equal(item.HeldMobyClass, I32(row, 0x10));
            Assert.Equal(item.NextDefinition, U16(row, 0x46));
            Assert.Equal(item.PreviousDefinition, U16(row, 0x48));
            Assert.Equal(item.AmmoCapacity, U16(row, 0x8a));
        }

        byte[] cannon = Virtual(overlay, 0x002c7e90, 0xf0);
        Hash(cannon, "2f8689a600a05ef1510a90b236327cd1203649c9196741e5d67efa280afc5a4d");
        Assert.Equal(Rac3ShockCannonProfile.AmmoPickupAmount, U16(cannon, 0x88));
        Assert.Equal(Rac3ShockCannonProfile.PrimaryAttackSequence, I32(cannon, 0x24));
        Assert.Equal(-1, I32(cannon, 0x28));
        Assert.Equal(Rac3ShockCannonProfile.SecondarySequenceField, I32(cannon, 0x2c));
        Assert.Equal(Rac3ShockCannonProfile.BaseDamage, F32(cannon, 0x6c));
        Assert.Equal(0, I32(cannon, 0x68)); // terminal ordinary V5 XP threshold
        Assert.Equal(1000, I32(Virtual(overlay, 0x002c7da0, 0xf0), 0x68));

        byte[] boot = disc.Read(1463L * 0x800, 771008);
        Hash(boot, "200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d");
        byte[] resident = WadLz.Decompress(boot[0x7080..], 8 * 1024 * 1024).Data;
        Hash(resident, "39ddc0784e58f7f94fd4c17341060a7d02a4afcd480bdf693c5ae5173c7d21f9");
        Assert.Equal(Enumerable.Range(0, 156).Select(i => (byte)i), Virtual(resident, 0x001425c0, 156));
        // Native upgrade admission follows row +0x46, writes the variant byte
        // for the original resource slot, and refills its new +0x8A capacity.
        Hash(Virtual(overlay, 0x00416640, 0xb0), "3e6067e32dd8841a974c0c048d1017f49d48895de4d8bc09a6317a6f7fe17846");
    }

    [SkippableFact]
    public void OriginalDiscPinsFireStateMachineDamageQueueAndSourceLocalAudioCalls()
    {
        using FileRandomAccessReader disc = OpenAuthority();
        byte[] overlay = ReadTable1Overlay(disc);
        Hash(Virtual(overlay, 0x00385948, 0x16b0), "36d5d5eaab00677426e63db9ae8481309bf40aa60fe378db43cfba243887a7ed");
        Hash(Virtual(overlay, 0x00386fb0, 0x238), "d6d642bd88d730f5bdc84890a9d95556fae4d1653960afb0407c92a2460efd2a");
        Hash(Virtual(overlay, 0x0050d8f8, 0x80), "e0198ecd59f0ddcf4b4b296935282b7aaa04603308f20d2f402663df6414245c");
        Hash(Virtual(overlay, 0x00443840, 0xf8), "ce9708f770dc44b2c03731829264a79342ba9e40eeebcd0e07adadfca72594ac");
        Assert.Equal(0x00385948u, U32(Virtual(overlay, 0x002f6ed8, 4)));
        Assert.Equal(0x1882u, U32(Virtual(overlay, 0x002f6ed4, 4)));
        Assert.Equal(Rac3ShockCannonProfile.ChargeUpdates, Immediate(overlay, 0x00385bbc));
        Assert.Equal(Rac3ShockCannonProfile.ChargedAmmoAttempts - 1, Immediate(overlay, 0x003860d4));
        Assert.Equal(Rac3ShockCannonProfile.BeamUpdates, Immediate(overlay, 0x003861a8));
        Assert.Equal(Rac3ShockCannonProfile.RecoveryUpdates, Immediate(overlay, 0x003869d8));
        Assert.Equal(Rac3ShockCannonProfile.BeamSampleCount, Immediate(overlay, 0x003864c8));
        Assert.Equal(Rac3ShockCannonProfile.RandomRayCount - 1, Immediate(overlay, 0x00386cc8));
        Assert.Equal(Rac3ShockCannonProfile.FixedRayCount, Immediate(overlay, 0x00386ee0));
        Assert.Equal(Rac3ShockCannonProfile.SpreadDamageFlags >> 16, (uint)Immediate(overlay, 0x00386a04));
        Assert.Equal(Rac3ShockCannonProfile.BeamDamageFlags >> 16, (uint)Immediate(overlay, 0x003863bc));
        Assert.Equal(0x0050d8f8u, JalTarget(overlay, 0x003860e0));
        Assert.Equal(0x0050d8f8u, JalTarget(overlay, 0x00386104));
        Assert.Equal(0x00443840u, JalTarget(overlay, 0x0038714c));
        Assert.Equal(0x004a7200u, JalTarget(overlay, 0x00386978));
        Assert.Equal(0x004a7200u, JalTarget(overlay, 0x0038698c));

        byte[] timing = Virtual(overlay, 0x001dddf0, 0x14);
        Assert.Equal(Rac3ShockCannonProfile.PrimaryWindupUpdates, U16(timing, 0));
        Assert.Equal(Rac3ShockCannonProfile.AlternateWindupUpdates, U16(timing, 4));
        Assert.Equal(2, U16(timing, 8));
        Assert.Equal(8, U16(timing, 12));
        Assert.Equal(8, U16(timing, 16));
    }

    [SkippableFact]
    public void CannonIntentsReachTheExistingRetailTargetConsumerWithoutChangingTargetRules()
    {
        using FileRandomAccessReader disc = OpenAuthority();
        // This is an engine-neutral target integration fixture, not ordinary
        // Godot play or proof that the host traces the original shot geometry.
        var world = Rac3WorldImport.Build(disc, 1);
        var source = world.DynamicObjects!.First(m => m.NativeClassId == 500);
        var runtime = new Rac3MobyRuntimeSession();
        runtime.Register(source);
        _ = new Rac3Class500DestructibleSession(runtime);
        var weapon = new Rac3ShockCannonSession();
        weapon.Equip();
        Rac3ShockCannonFrame? frame = null;
        for (int i = 0; i < 79; i++) frame = weapon.Advance(new(true));
        var target = new Rac3ShockCannonTarget(ObpSourceGame.Rac3, source.NativeClassId, source.InstanceIndex);
        var intent = Assert.Single(weapon.ResolveContacts(frame!, [new(0, target, 2, true, 0x4000)]));
        Rac3Class500BreakResult result = runtime.DispatchDamage<Rac3Class500BreakResult>(intent.ToRac3DamageEvent(0));
        Assert.Equal(Rac3Class500Destructible.BreakTransitionNativeState, result.NativeBreakTransitionState);
        Assert.False(runtime.Require(source).IsActive);
    }

    private static FileRandomAccessReader OpenAuthority()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_UYA_ISO");
        Skip.If(string.IsNullOrWhiteSpace(iso), "OBP_UYA_ISO not set");
        var disc = new FileRandomAccessReader(iso!);
        Assert.Equal(Rac3Authority.PrimaryIsoSizeBytes, disc.Length);
        return disc;
    }

    private static byte[] ReadTable1Overlay(IRandomAccessReader disc)
    {
        var main = Rac3DiscIndex.OpenMainLevel(disc, 1).Reader;
        byte[] header = main.Read(0, 0x60);
        var primary = new SubRangeReader(main, (long)I32(header, 0x10) * 0x800, (long)I32(header, 0x14) * 0x800);
        byte[] range = primary.Read(0, 8);
        Assert.InRange(I32(range, 4), 0x10, 16 * 1024 * 1024);
        byte[] overlay = primary.Read(I32(range, 0), I32(range, 4));
        Hash(overlay, "093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec");
        return overlay;
    }

    private static byte[] ReadEnglishText(IRandomAccessReader disc)
    {
        byte[] hud = disc.Read(1001L * 0x800 + 0x4950, 0x2ab0);
        Assert.Equal(0x2ab0, I32(hud, 0));
        Assert.Equal(0x238f, I32(hud, 0x1838));
        Assert.Equal(136, I32(hud, 0x183c));
        byte[] text = disc.Read(((long)I32(hud, 4) + I32(hud, 0x1838)) * 0x800, I32(hud, 0x183c) * 0x800);
        Hash(text, "86e0709dcf702e10a5dac79b15dced6de014bcfb5b7c293ab623a6dbd58c135e");
        return text;
    }

    private static string TextForId(byte[] table, int id)
    {
        int directoryEnd = I32(table, 0);
        for (int at = 0; at < directoryEnd; at += 0x10)
        {
            if (I32(table, at + 4) != id) continue;
            int start = I32(table, at);
            int end = Array.IndexOf(table, (byte)0, start);
            return Encoding.ASCII.GetString(table, start, end - start);
        }
        throw new InvalidDataException($"UYA text ID 0x{id:x} absent.");
    }

    // Test-only bounded view; boot includes reginfo sections whereas TABLE1
    // consists of copy/BSS sections. No dependency on the GC source module.
    private static byte[] Virtual(byte[] image, uint address, int count)
    {
        for (int at = 0; at + 0x10 <= image.Length;)
        {
            uint destination = U32(image, at);
            int size = I32(image, at + 4);
            uint kind = U32(image, at + 8);
            Assert.InRange(size, 0, image.Length - at - 0x10);
            if (kind is 1 or 8 && address >= destination && (ulong)address + (uint)count <= (ulong)destination + (uint)size)
                return image.AsSpan(at + 0x10 + (int)(address - destination), count).ToArray();
            at = checked(at + 0x10 + size);
        }
        throw new InvalidDataException($"UYA loaded range 0x{address:x}+0x{count:x} absent.");
    }

    private static int Immediate(byte[] overlay, uint address) => U16(Virtual(overlay, address, 4), 0);
    private static uint JalTarget(byte[] overlay, uint address)
    {
        uint instruction = U32(Virtual(overlay, address, 4));
        Assert.Equal(3u, instruction >> 26);
        return (instruction & 0x03ffffff) << 2;
    }
    private static int I32(byte[] bytes, int at = 0) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static uint U32(byte[] bytes, int at = 0) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static float F32(byte[] bytes, int at) => BitConverter.Int32BitsToSingle(I32(bytes, at));
    private static void Hash(byte[] bytes, string expected) => Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
}
