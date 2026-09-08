using System.Buffers.Binary;
using OBP.IO;

namespace OBP.PS2.Iso;

public sealed record Iso9660DirectoryEntry(
    string RawIdentifier,
    string Name,
    int? Version,
    long ExtentLba,
    long DataLength,
    int Flags,
    bool IsDirectory,
    bool IsSpecial);

public sealed record Iso9660PrimaryVolumeDescriptor(
    long DescriptorLba,
    string SystemIdentifier,
    string VolumeIdentifier,
    long VolumeSpaceSize,
    int LogicalBlockSize,
    Iso9660DirectoryEntry RootDirectory);

/// <summary>Range-limited view of one ISO-9660 file extent.</summary>
public sealed class Iso9660ExtentReader : IRandomAccessReader
{
    private readonly IRandomAccessReader _source;
    private readonly long _start;

    public string Name { get; }
    public long Length { get; }

    public Iso9660ExtentReader(IRandomAccessReader source, Iso9660DirectoryEntry entry, int logicalBlockSize, string? name = null)
    {
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException($"ISO-9660 entry '{entry.Name}' is a directory, not a file extent.");
        }

        long start = entry.ExtentLba * logicalBlockSize;
        if (start < 0 || start > source.Length || entry.DataLength > source.Length - start)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), $"File extent '{entry.Name}' lies outside {source.Name}.");
        }

        _source = source;
        _start = start;
        Length = entry.DataLength;
        Name = name ?? entry.Name;
    }

    public void Read(long offset, Span<byte> destination)
    {
        RandomAccessReaderExtensions.ValidateRange(Name, Length, offset, destination.Length);
        _source.Read(_start + offset, destination);
    }
}

/// <summary>
/// Path-oriented ISO-9660 facade. Caches only the primary descriptor; every
/// directory traversal stays block-bounded and file contents remain
/// range-readable. Mirrors the TypeScript <c>Iso9660Filesystem</c>.
/// </summary>
public sealed class Iso9660Filesystem
{
    private const long VolumeDescriptorStartLba = 16;
    private const int VolumeDescriptorBytes = 2048;
    private const string StandardIdentifier = "CD001";

    private readonly IRandomAccessReader _source;

    public Iso9660PrimaryVolumeDescriptor Volume { get; }

    private Iso9660Filesystem(IRandomAccessReader source, Iso9660PrimaryVolumeDescriptor volume)
    {
        _source = source;
        Volume = volume;
    }

    public static Iso9660Filesystem Open(IRandomAccessReader source) =>
        new(source, ReadPrimaryVolumeDescriptor(source));

    public static Iso9660PrimaryVolumeDescriptor ReadPrimaryVolumeDescriptor(IRandomAccessReader reader, int maxDescriptors = 64)
    {
        var sector = new byte[VolumeDescriptorBytes];
        for (int index = 0; index < maxDescriptors; index++)
        {
            long descriptorLba = VolumeDescriptorStartLba + index;
            long offset = descriptorLba * VolumeDescriptorBytes;
            if (offset + VolumeDescriptorBytes > reader.Length)
            {
                break;
            }

            reader.Read(offset, sector);
            string identifier = Ascii(sector, 1, 5);
            if (identifier != StandardIdentifier)
            {
                throw new InvalidDataException($"Invalid ISO-9660 standard identifier '{identifier}' at LBA {descriptorLba}.");
            }

            if (sector[6] != 1)
            {
                throw new InvalidDataException($"Unsupported ISO-9660 descriptor version {sector[6]} at LBA {descriptorLba}.");
            }

            int type = sector[0];
            if (type == 255)
            {
                break;
            }

            if (type != 1)
            {
                continue;
            }

            long volumeSpaceSize = BothEndianUInt32(sector, 80, 84, "volume space size");
            int logicalBlockSize = BothEndianUInt16(sector, 128, 130, "logical block size");
            if (logicalBlockSize <= 0)
            {
                throw new InvalidDataException("ISO-9660 logical block size must be positive.");
            }

            var root = ParseDirectoryRecord(sector, 156);
            if (!root.IsDirectory)
            {
                throw new InvalidDataException("ISO-9660 primary volume descriptor root record is not a directory.");
            }

            return new Iso9660PrimaryVolumeDescriptor(
                descriptorLba,
                Ascii(sector, 8, 32).TrimEnd(),
                Ascii(sector, 40, 32).TrimEnd(),
                volumeSpaceSize,
                logicalBlockSize,
                root);
        }

        throw new InvalidDataException("ISO-9660 primary volume descriptor was not found.");
    }

    public Iso9660DirectoryEntry? Find(string path)
    {
        var segments = NormalizePath(path);
        var current = Volume.RootDirectory;
        if (segments.Count == 0)
        {
            return current;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (!current.IsDirectory)
            {
                return null;
            }

            var next = ReadDirectoryEntries(current).FirstOrDefault(e => IdentifierMatches(e, segments[i]));
            if (next is null)
            {
                return null;
            }

            if (i < segments.Count - 1 && !next.IsDirectory)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public IReadOnlyList<Iso9660DirectoryEntry>? List(string path = "/")
    {
        var dir = Find(path);
        if (dir is null)
        {
            return null;
        }

        if (!dir.IsDirectory)
        {
            throw new InvalidOperationException($"ISO-9660 path '{path}' is not a directory.");
        }

        return ReadDirectoryEntries(dir);
    }

    public Iso9660ExtentReader? OpenFile(string path)
    {
        var entry = Find(path);
        if (entry is null)
        {
            return null;
        }

        if (entry.IsDirectory)
        {
            throw new InvalidOperationException($"ISO-9660 path '{path}' is a directory, not a file.");
        }

        return new Iso9660ExtentReader(_source, entry, Volume.LogicalBlockSize, string.Join('/', NormalizePath(path)));
    }

    public IReadOnlyList<Iso9660DirectoryEntry> ReadDirectoryEntries(Iso9660DirectoryEntry directory, bool includeSpecial = false)
    {
        if (!directory.IsDirectory)
        {
            throw new InvalidOperationException($"ISO-9660 entry '{directory.Name}' is not a directory.");
        }

        int blockSize = Volume.LogicalBlockSize;
        long extentOffset = directory.ExtentLba * blockSize;
        if (extentOffset < 0 || directory.DataLength < 0 || extentOffset > _source.Length || directory.DataLength > _source.Length - extentOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(directory), $"Directory extent '{directory.Name}' lies outside {_source.Name}.");
        }

        var entries = new List<Iso9660DirectoryEntry>();
        long consumed = 0;
        var block = new byte[blockSize];
        while (consumed < directory.DataLength)
        {
            int readLength = (int)System.Math.Min(blockSize, directory.DataLength - consumed);
            _source.Read(extentOffset + consumed, block.AsSpan(0, readLength));

            int offset = 0;
            while (offset < readLength)
            {
                int recordLength = block[offset];
                if (recordLength == 0)
                {
                    break;
                }

                if (offset + recordLength > readLength)
                {
                    throw new InvalidDataException($"ISO-9660 directory record crosses a logical-block boundary at {extentOffset + consumed + offset}.");
                }

                var entry = ParseDirectoryRecord(block, offset);
                if (includeSpecial || !entry.IsSpecial)
                {
                    entries.Add(entry);
                }

                offset += recordLength;
            }

            consumed += readLength;
        }

        return entries;
    }

    public static IReadOnlyList<string> NormalizePath(string path)
    {
        if (path.Contains('\0'))
        {
            throw new InvalidDataException("ISO-9660 path contains a NUL byte.");
        }

        string normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
        {
            throw new InvalidDataException($"ISO-9660 path traversal is not allowed: '{path}'.");
        }

        return segments;
    }

    private static bool IdentifierMatches(Iso9660DirectoryEntry entry, string segment)
    {
        string expected = segment.ToUpperInvariant();
        return entry.Name.ToUpperInvariant() == expected || entry.RawIdentifier.ToUpperInvariant() == expected;
    }

    private static Iso9660DirectoryEntry ParseDirectoryRecord(ReadOnlySpan<byte> bytes, int offset)
    {
        int recordLength = bytes[offset];
        if (recordLength < 34 || offset + recordLength > bytes.Length)
        {
            throw new InvalidDataException($"Invalid ISO-9660 directory record length {recordLength} at offset {offset}.");
        }

        int identifierLength = bytes[offset + 32];
        if (identifierLength == 0 || 33 + identifierLength > recordLength)
        {
            throw new InvalidDataException($"Invalid ISO-9660 file identifier length {identifierLength} at offset {offset}.");
        }

        long extentLba = BothEndianUInt32(bytes, offset + 2, offset + 6, "extent location");
        long dataLength = BothEndianUInt32(bytes, offset + 10, offset + 14, "data length");
        int flags = bytes[offset + 25];

        var idBytes = bytes.Slice(offset + 33, identifierLength);
        string rawIdentifier;
        string name;
        int? version = null;
        bool isSpecial = false;

        if (identifierLength == 1 && idBytes[0] == 0)
        {
            rawIdentifier = "\\0";
            name = ".";
            isSpecial = true;
        }
        else if (identifierLength == 1 && idBytes[0] == 1)
        {
            rawIdentifier = "\\1";
            name = "..";
            isSpecial = true;
        }
        else
        {
            rawIdentifier = Ascii(idBytes, 0, idBytes.Length);
            int semi = rawIdentifier.LastIndexOf(';');
            if (semi >= 0 && int.TryParse(rawIdentifier.AsSpan(semi + 1), out int v))
            {
                name = rawIdentifier[..semi];
                version = v;
            }
            else
            {
                name = rawIdentifier;
            }
        }

        return new Iso9660DirectoryEntry(rawIdentifier, name, version, extentLba, dataLength, flags, (flags & 0x02) != 0, isSpecial);
    }

    private static int BothEndianUInt16(ReadOnlySpan<byte> b, int littleOffset, int bigOffset, string label)
    {
        ushort little = BinaryPrimitives.ReadUInt16LittleEndian(b[littleOffset..]);
        ushort big = BinaryPrimitives.ReadUInt16BigEndian(b[bigOffset..]);
        if (little != big)
        {
            throw new InvalidDataException($"ISO-9660 {label} endian copies disagree: {little} != {big}.");
        }

        return little;
    }

    private static long BothEndianUInt32(ReadOnlySpan<byte> b, int littleOffset, int bigOffset, string label)
    {
        uint little = BinaryPrimitives.ReadUInt32LittleEndian(b[littleOffset..]);
        uint big = BinaryPrimitives.ReadUInt32BigEndian(b[bigOffset..]);
        if (little != big)
        {
            throw new InvalidDataException($"ISO-9660 {label} endian copies disagree: {little} != {big}.");
        }

        return little;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        Span<char> chars = length <= 256 ? stackalloc char[length] : new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)bytes[offset + i];
        }

        return new string(chars);
    }
}
