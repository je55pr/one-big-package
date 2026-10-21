using System.Buffers.Binary;
using OBP.IO;
using OBP.PS2.Audio;
using OBP.RAC1.Level;
using OBP.Runtime.Audio;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Original R&C1 Novalis CD music table. Level header +0x148 contains 9 VAGp
/// sector addresses: three start/loop pairs and three transition stingers.
/// Matches native music_start/music_queue_next (music_streams.cpp, 0x265418).
/// No retail audio payload is shipped with OBP.
/// </summary>
public sealed class Rac1NovalisMusic
{
    public const int MusicTableOffset = 0x148;
    public const int TrackCount = 9;
    public const int SectorBytes = 2048;
    private const int MaxEncodedBytes = 8 * 1024 * 1024;

    public enum Area { Enemy, Cave, Waterworks }

    public sealed record Track(int Index, uint Sector, Vagp.Header Header)
    {
        public double DurationSeconds => (double)Header.DataSize / Ps2SpuAdpcm.FrameSize *
            Ps2SpuAdpcm.SamplesPerFrame / Header.SampleRate;
    }

    private readonly IRandomAccessReader _disc;
    private readonly Track[] _tracks;
    public IReadOnlyList<Track> Tracks => _tracks;

    private Rac1NovalisMusic(IRandomAccessReader disc, Track[] tracks)
    {
        _disc = disc;
        _tracks = tracks;
    }

    public static Rac1NovalisMusic Open(IRandomAccessReader disc, Rac1DiscIndex.NativeLevel level)
    {
        ArgumentNullException.ThrowIfNull(disc);
        if (level.LevelId != 1) throw new ArgumentException("Only original Novalis LEVEL1 is admitted.", nameof(level));
        byte[] header = disc.Read(checked((long)level.HeaderLba * SectorBytes),
            Rac1DiscIndex.NativeLevelHeaderSize);
        var tracks = new Track[TrackCount];
        uint previous = 0;
        for (int i = 0; i < TrackCount; i++)
        {
            uint sector = BinaryPrimitives.ReadUInt32LittleEndian(
                header.AsSpan(MusicTableOffset + i * 4, 4));
            if (sector <= previous || sector == 0 ||
                (long)sector * SectorBytes + Vagp.HeaderSize > disc.Length)
                throw new InvalidDataException($"Invalid Novalis music sector for slot {i}: {sector}");
            long remaining = disc.Length - (long)sector * SectorBytes;
            var stream = Vagp.Open(new SubRangeReader(disc, (long)sector * SectorBytes,
                Math.Min(remaining, (long)MaxEncodedBytes + Vagp.HeaderSize), $"novalis music {i}"));
            if (stream.Header.DataSize > MaxEncodedBytes)
                throw new InvalidDataException($"Novalis music slot {i} exceeds bounded stream size.");
            tracks[i] = new Track(i, sector, stream.Header);
            previous = sector;
        }
        for (int i = 0; i < TrackCount - 1; i++)
        {
            long end = (long)tracks[i].Sector * SectorBytes + Vagp.HeaderSize + tracks[i].Header.DataSize;
            if (end > (long)tracks[i + 1].Sector * SectorBytes)
                throw new InvalidDataException($"Novalis music slot {i} overlaps slot {i + 1}.");
        }
        return new Rac1NovalisMusic(disc, tracks);
    }

    public (int Intro, int Loop) Pair(Area area) => area switch
    {
        Area.Enemy => (0, 1),
        Area.Cave => (2, 3),
        Area.Waterworks => (4, 5),
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    public RuntimeAudioClip Decode(int slot)
    {
        if ((uint)slot >= _tracks.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        Track track = _tracks[slot];
        int bytes = checked(track.Header.DataSize + Vagp.HeaderSize);
        var source = new SubRangeReader(_disc, (long)track.Sector * SectorBytes, bytes,
            $"novalis-{slot}-{track.Header.Name}");
        var vag = Vagp.Open(source);
        return new RuntimeAudioClip(vag.Header.SampleRate, 1, vag.DecodeMono());
    }
}
