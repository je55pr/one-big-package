using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2;
using OBP.RAC1;
using OBP.RAC1.Animation;
using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1MobyAnimationSoundTests
{
    private sealed record SequenceSnapshot(
        int Index,
        int FrameCount,
        byte SoundId,
        uint Opaque14,
        (ushort PositionUnits, ushort SoundId)[] TimedCues);

    private sealed record ClassSnapshot(
        int Level,
        int OClass,
        int JointCount,
        int SoundCount,
        SequenceSnapshot[] Sequences);

    [Fact]
    public void SyntheticClassSoundAndTimedCueFieldsUseRecoveredSemantics()
    {
        var bytes = new byte[0x180];
        bytes[0x0c] = 1;
        bytes[0x0d] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x28), 0x100);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x48), 0x80);

        bytes[0x80 + 0x10] = 0;
        bytes[0x80 + 0x11] = 2;
        bytes[0x80 + 0x12] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80 + 0x14), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80 + 0x1c), (0x0040u << 16) | 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80 + 0x20), (0x00a0u << 16) | 2u);

        var sounds = Rac1MobyAnimation.ReadClassSoundTable(bytes);
        Assert.Equal(3, sounds.Count);
        Assert.Equal(0x100, sounds.Offset);
        Assert.Equal([0x100, 0x120, 0x140], sounds.Definitions.Select(row => row.Offset));

        var sequence = Assert.IsType<Rac1MobyAnimation.Sequence>(
            Assert.Single(Rac1MobyAnimation.ReadSequences(bytes)).Value);
        Assert.Equal(2, sequence.SoundId);
        Assert.True(sequence.HasDirectSound);
        Assert.Equal(2, sequence.TriggerCount);
        Assert.Equal(0x12345678u, sequence.Opaque14);
        Assert.Equal([(0x0040, 1), (0x00a0, 2)],
            sequence.TimedSoundCues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
        Assert.Equal([4f, 10f], sequence.TimedSoundCues.Select(cue => cue.PositionFrames));
    }
    [SkippableFact]
    public void AuthorizedRetailAnimationSoundCensusMatchesStaticRecovery()
    {
        string? iso = Environment.GetEnvironmentVariable("OBP_RAC1_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_RAC1_ISO not set");

        using var reader = new FileRandomAccessReader(iso!);
        Assert.Equal(Rac1Authority.PrimaryIsoSizeBytes, reader.Length);
        Assert.Equal(
            Rac1Authority.Primary.Serial,
            Ps2Boot.ReadBootInfo(reader).Serial,
            ignoreCase: true);

        var snapshots = new List<ClassSnapshot>();
        var instantiated = new HashSet<int>();
        var withSoundTables = new HashSet<int>();
        var withDirectSounds = new HashSet<int>();
        var withTimedSounds = new HashSet<int>();
        long payloadOccurrences = 0;
        long populatedSequences = 0;
        long timedSoundWords = 0;
        long outOfRangeDirect = 0;
        long outOfRangeTimed = 0;

        foreach (var level in Rac1DiscIndex.Read(reader).Levels.OrderBy(level => level.LevelId))
        {
            var core = Rac1LevelCore.Open(reader, level);
            var classes = Rac1StaticClasses.Read(core).Mobies;
            var gameplay = Rac1Instances.Parse(Rac1LevelSettings.ReadGameplay(reader, level));
            instantiated.UnionWith(gameplay.MobyInstances.Select(instance => instance.OClass));
            payloadOccurrences += classes.Count;

            foreach (var cls in classes.Values)
            {
                if (cls.SoundTable.Count > 0)
                    withSoundTables.Add(cls.OClass);

                var sequenceRows = cls.Sequences
                    .Where(slot => slot.Value is not null)
                    .Select(slot => slot.Value!)
                    .ToArray();
                populatedSequences += sequenceRows.Length;

                foreach (var sequence in sequenceRows)
                {
                    if (sequence.HasDirectSound)
                    {
                        withDirectSounds.Add(cls.OClass);
                        if (sequence.SoundId >= cls.SoundTable.Count)
                            outOfRangeDirect++;
                    }

                    if (sequence.TimedSoundCues.Count > 0)
                        withTimedSounds.Add(cls.OClass);
                    timedSoundWords += sequence.TimedSoundCues.Count;
                    outOfRangeTimed += sequence.TimedSoundCues.Count(
                        cue => cue.SoundId >= cls.SoundTable.Count);
                }
                snapshots.Add(new ClassSnapshot(
                    level.LevelId,
                    cls.OClass,
                    cls.JointCount,
                    cls.SoundTable.Count,
                    sequenceRows.Select(sequence => new SequenceSnapshot(
                        sequence.Index,
                        sequence.Frames.Count,
                        sequence.SoundId,
                        sequence.Opaque14,
                        sequence.TimedSoundCues
                            .Select(cue => (cue.PositionUnits, cue.SoundId))
                            .ToArray()))
                        .ToArray()));
            }
        }

        Assert.Equal(2_968, payloadOccurrences);
        Assert.Equal(10_745, populatedSequences);
        Assert.Equal(1_960, timedSoundWords);
        Assert.Equal(456, withSoundTables.Count);
        Assert.Equal(49, withDirectSounds.Count);
        Assert.Equal(89, withTimedSounds.Count);
        Assert.Equal(21, withDirectSounds.Intersect(withTimedSounds).Count());
        Assert.Equal(0, outOfRangeDirect);
        Assert.Equal(0, outOfRangeTimed);
        Assert.Equal(371, withSoundTables.Intersect(instantiated).Count());
        Assert.Equal(47, withDirectSounds.Intersect(instantiated).Count());
        Assert.Equal(80, withTimedSounds.Intersect(instantiated).Count());
        var opaque14 = snapshots
            .SelectMany(cls => cls.Sequences
                .Where(sequence => sequence.Opaque14 != 0)
                .Select(sequence => (cls.Level, cls.OClass, sequence.Index, sequence.Opaque14)))
            .ToArray();
        Assert.Equal(4, opaque14.Length);
        Assert.All(opaque14, row => Assert.Equal(4, row.Level));
        Assert.Equal([217, 563], opaque14.Select(row => row.OClass).Distinct().Order().ToArray());

        AssertShape(snapshots, 749, joints: 53, sequences: 8, sounds: 9);
        foreach (var cls in ForClass(snapshots, 749))
        {
            var attack = Assert.Single(cls.Sequences, sequence => sequence.Index == 5);
            Assert.Equal([(64, 6), (160, 0)],
                attack.TimedCues.Select(cue => ((int)cue.PositionUnits, (int)cue.SoundId)));
        }

        AssertShape(snapshots, 1440, joints: 35, sequences: 13, sounds: 11);
        foreach (var cls in ForClass(snapshots, 1440))
        {
            Assert.Equal(5, Assert.Single(cls.Sequences, sequence => sequence.Index == 6).SoundId);
            Assert.Equal(10, Assert.Single(cls.Sequences, sequence => sequence.Index == 9).SoundId);
            Assert.Contains(cls.Sequences, sequence => sequence.TimedCues.Length > 0);
        }

        AssertShape(snapshots, 572, joints: 77, sequences: 4, sounds: 6);
        AssertShape(snapshots, 865, joints: 75, sequences: 6, sounds: 7);
        AssertShape(snapshots, 866, joints: 78, sequences: 7, sounds: 7);
        AssertDirectSequence(snapshots, 572, sequence: 1, soundId: 4);
        AssertDirectSequence(snapshots, 865, sequence: 1, soundId: 5);
        AssertDirectSequence(snapshots, 866, sequence: 1, soundId: 5);
        Assert.Equal(3, new[] { 572, 865, 866 }
            .Select(id => TimedSchedule(ForClass(snapshots, id)[0]))
            .Distinct()
            .Count());

        foreach (int id in new[] { 1781, 1782 })
        {
            AssertShape(snapshots, id, joints: 13, sequences: 2, sounds: 0);
            AssertNoAnimationLinkedSounds(snapshots, id);
        }

        int[] crateFamily = [500, 501, 502, 505, 511];
        int[] crateSoundCounts = [1, 2, 2, 2, 1];
        for (int i = 0; i < crateFamily.Length; i++)
        {
            int id = crateFamily[i];
            AssertShape(snapshots, id, joints: 0, sequences: 1, sounds: crateSoundCounts[i]);
            Assert.All(ForClass(snapshots, id), cls => Assert.Equal(1, Assert.Single(cls.Sequences).FrameCount));
            AssertNoAnimationLinkedSounds(snapshots, id);
        }

        foreach (int id in new[] { 224, 228 })
        {
            Assert.All(ForClass(snapshots, id), cls =>
            {
                Assert.Equal(0, cls.SoundCount);
                Assert.Equal(1, Assert.Single(cls.Sequences).FrameCount);
            });
            AssertNoAnimationLinkedSounds(snapshots, id);
        }
        Assert.All(ForClass(snapshots, 758), cls =>
        {
            Assert.Equal(1, cls.JointCount);
            Assert.Equal(1, cls.SoundCount);
            Assert.Equal(9, Assert.Single(cls.Sequences).FrameCount);
        });
        Assert.All(ForClass(snapshots, 803), cls =>
        {
            Assert.Equal(0, cls.JointCount);
            Assert.Equal(1, cls.SoundCount);
            Assert.Equal(1, Assert.Single(cls.Sequences).FrameCount);
        });
        AssertNoAnimationLinkedSounds(snapshots, 758);
        AssertNoAnimationLinkedSounds(snapshots, 803);
    }

    private static ClassSnapshot[] ForClass(IEnumerable<ClassSnapshot> snapshots, int oClass)
    {
        var rows = snapshots.Where(row => row.OClass == oClass).OrderBy(row => row.Level).ToArray();
        Assert.NotEmpty(rows);
        return rows;
    }

    private static void AssertShape(
        IEnumerable<ClassSnapshot> snapshots,
        int oClass,
        int joints,
        int sequences,
        int sounds)
    {
        Assert.All(ForClass(snapshots, oClass), cls =>
        {
            Assert.Equal(joints, cls.JointCount);
            Assert.Equal(sequences, cls.Sequences.Length);
            Assert.Equal(sounds, cls.SoundCount);
        });
    }
    private static void AssertDirectSequence(
        IEnumerable<ClassSnapshot> snapshots,
        int oClass,
        int sequence,
        byte soundId)
    {
        Assert.All(ForClass(snapshots, oClass), cls =>
            Assert.Equal(soundId, Assert.Single(cls.Sequences, row => row.Index == sequence).SoundId));
    }

    private static void AssertNoAnimationLinkedSounds(
        IEnumerable<ClassSnapshot> snapshots,
        int oClass)
    {
        Assert.All(ForClass(snapshots, oClass), cls =>
        {
            Assert.All(cls.Sequences, sequence =>
            {
                Assert.Equal(Rac1MobyAnimation.NoSoundId, sequence.SoundId);
                Assert.Empty(sequence.TimedCues);
            });
        });
    }

    private static string TimedSchedule(ClassSnapshot cls) =>
        string.Join(
            ";",
            cls.Sequences.SelectMany(sequence => sequence.TimedCues.Select(
                cue => $"{sequence.Index}:{cue.PositionUnits}:{cue.SoundId}")));
}
