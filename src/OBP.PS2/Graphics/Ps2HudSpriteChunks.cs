using OBP.IO;
using OBP.PS2.Compression;

namespace OBP.PS2.Graphics;

/// <summary>Bounded raw header and five WAD-LZ payloads. Source owners supply their own container ranges.</summary>
public static class Ps2HudSpriteChunks
{
    public static Ps2HudSpriteBank Read(IRandomAccessReader container, IReadOnlyList<(long Offset, int Length)> ranges)
    {
        if (ranges.Count != 6) throw new InvalidDataException("A HUD bank needs six chunk ranges.");
        var chunks = new byte[6][];
        for (int i = 0; i < chunks.Length; i++)
        {
            var (offset, length) = ranges[i];
            if (length < 0 || length > 8 * 1024 * 1024 || offset < 0 || offset > container.Length || length > container.Length - offset)
                throw new InvalidDataException("HUD chunk exceeds its source container or size limit.");
            chunks[i] = length == 0 ? [] : container.Read(offset, length);
            if (i > 0 && length > 0) chunks[i] = WadLz.Decompress(chunks[i], 32 * 1024 * 1024).Data;
        }
        return Ps2HudSpriteBank.Decode(chunks);
    }
}
