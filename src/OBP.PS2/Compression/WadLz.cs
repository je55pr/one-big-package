using OBP.IO;

namespace OBP.PS2.Compression;

/// <summary>
/// Decompressor for the "WAD" LZ container used by the PS2 Ratchet &amp; Clank
/// games for level chunk data, level-core asset blocks and similar. Translated
/// directly from <c>reference-ts/packages/wad-lz</c> — see that file for the
/// packet grammar; behaviour must stay byte-identical.
///
/// Container header (16 bytes): <c>'W' 'A' 'D'</c>, then a little-endian
/// <c>s32 compressedSize</c> at the unaligned offset 3, then 9 informational
/// name/pad bytes, then the packet stream up to <c>compressedSize</c>.
/// </summary>
public static class WadLz
{
    public const int HeaderSize = 0x10;
    private const long DefaultMaxOutputBytes = 128L * 1024 * 1024;

    public readonly record struct WadLzHeader(int CompressedSize, string Name);

    public sealed record WadLzResult(byte[] Data, int CompressedSize, string Name);

    public static bool IsWadLz(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize || bytes[0] != 0x57 || bytes[1] != 0x41 || bytes[2] != 0x44)
        {
            return false;
        }

        int compressedSize = ReadInt32LeUnaligned(bytes, 3);
        return compressedSize >= HeaderSize && compressedSize <= bytes.Length;
    }

    public static WadLzHeader ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            throw new InvalidDataException("WAD LZ source shorter than the 16-byte header.");
        }

        if (bytes[0] != 0x57 || bytes[1] != 0x41 || bytes[2] != 0x44)
        {
            throw new InvalidDataException("WAD LZ magic 'WAD' not found.");
        }

        int compressedSize = ReadInt32LeUnaligned(bytes, 3);
        if (compressedSize < HeaderSize)
        {
            throw new InvalidDataException($"WAD LZ compressed size {compressedSize} is smaller than the header.");
        }

        int nameLen = 0;
        while (nameLen < 9 && bytes[7 + nameLen] != 0)
        {
            nameLen++;
        }

        var name = string.Create(nameLen, bytes.Slice(7, nameLen).ToArray(), (span, src) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = (char)src[i];
            }
        });

        return new WadLzHeader(compressedSize, name);
    }

    /// <summary>Decompress a complete in-memory WAD LZ block.</summary>
    public static WadLzResult Decompress(byte[] bytes, long maxOutputBytes = DefaultMaxOutputBytes)
    {
        var header = ReadHeader(bytes);
        if (header.CompressedSize > bytes.Length)
        {
            throw new InvalidDataException($"WAD LZ compressed size {header.CompressedSize} exceeds the {bytes.Length}-byte source.");
        }

        int end = header.CompressedSize;
        int pos = HeaderSize;

        var out_ = new byte[(int)System.Math.Min(maxOutputBytes, System.Math.Max(0x1000, (long)header.CompressedSize * 4))];
        int outLen = 0;

        void Ensure(int extra)
        {
            if (outLen + (long)extra > maxOutputBytes)
            {
                throw new InvalidDataException($"WAD LZ output exceeds cap {maxOutputBytes}.");
            }

            if (outLen + extra <= out_.Length)
            {
                return;
            }

            long next = out_.Length;
            while (next < outLen + extra)
            {
                next = System.Math.Min(maxOutputBytes, next * 2);
            }

            var grown = new byte[next];
            Array.Copy(out_, grown, outLen);
            out_ = grown;
        }

        byte Read8()
        {
            if (pos >= end)
            {
                throw new InvalidDataException("WAD LZ: unexpected end of stream.");
            }

            return bytes[pos++];
        }

        void CopyLiteral(int count)
        {
            if (count == 0)
            {
                return;
            }

            if (pos + count > end)
            {
                throw new InvalidDataException("WAD LZ: literal run runs past the stream.");
            }

            Ensure(count);
            Array.Copy(bytes, pos, out_, outLen, count);
            outLen += count;
            pos += count;
        }

        bool sawLiteralPacket = false;

        while (pos < end)
        {
            int flag = Read8();

            if (flag < 0x10)
            {
                if (sawLiteralPacket)
                {
                    throw new InvalidDataException("WAD LZ: two literal packets in a row.");
                }

                int size = flag != 0 ? flag + 3 : Read8() + 18;
                CopyLiteral(size);
                sawLiteralPacket = true;
                continue;
            }

            sawLiteralPacket = false;

            int matchSize;
            long lookback;
            bool ended = false;

            if (flag < 0x20)
            {
                matchSize = flag & 7;
                if (matchSize == 0)
                {
                    matchSize = Read8() + 7;
                }

                int b0 = Read8();
                int b1 = Read8();
                lookback = outLen - ((flag & 8) << 11) - (b1 << 6) - (b0 >> 2);
                if (lookback != outLen)
                {
                    matchSize += 2;
                    lookback -= 0x4000;
                }
                else if (matchSize != 1)
                {
                    int body = pos - HeaderSize;
                    int aligned = (body + 0xfff) & ~0xfff;
                    pos = HeaderSize + aligned;
                    ended = true;
                }
            }
            else if (flag < 0x40)
            {
                matchSize = flag & 0x1f;
                if (matchSize == 0)
                {
                    matchSize = Read8() + 0x1f;
                }

                matchSize += 2;
                int b1 = Read8();
                int b2 = Read8();
                lookback = outLen - (b2 << 6) - (b1 >> 2) - 1;
            }
            else
            {
                int b1 = Read8();
                lookback = outLen - (b1 << 3) - ((flag >> 2) & 7) - 1;
                matchSize = (flag >> 5) + 1;
            }

            if (ended)
            {
                continue;
            }

            if (matchSize != 1)
            {
                if (lookback < 0 || lookback >= outLen)
                {
                    throw new InvalidDataException("WAD LZ: match points outside the output buffer.");
                }

                Ensure(matchSize);
                for (int i = 0; i < matchSize; i++)
                {
                    out_[outLen + i] = out_[lookback + i];
                }

                outLen += matchSize;
            }

            CopyLiteral(bytes[pos - 2] & 3);
        }

        var data = new byte[outLen];
        Array.Copy(out_, data, outLen);
        return new WadLzResult(data, header.CompressedSize, header.Name);
    }

    /// <summary>Read a WAD LZ block from <paramref name="reader"/> at <paramref name="offset"/> and decompress it — only the block's own bytes are read.</summary>
    public static WadLzResult ReadBlock(IRandomAccessReader reader, long offset, long maxOutputBytes = DefaultMaxOutputBytes)
    {
        if (offset < 0 || offset + HeaderSize > reader.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), $"WAD LZ offset {offset} lies outside '{reader.Name}'.");
        }

        Span<byte> head = stackalloc byte[HeaderSize];
        reader.Read(offset, head);
        var header = ReadHeader(head);
        if (offset + header.CompressedSize > reader.Length)
        {
            throw new InvalidDataException($"WAD LZ block at {offset} (compressed size {header.CompressedSize}) runs past '{reader.Name}'.");
        }

        var block = reader.Read(offset, header.CompressedSize);
        return Decompress(block, maxOutputBytes);
    }

    private static int ReadInt32LeUnaligned(ReadOnlySpan<byte> bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
}
