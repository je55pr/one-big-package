using System.Text.RegularExpressions;
using OBP.IO;
using OBP.PS2.Elf;
using OBP.PS2.Iso;

namespace OBP.PS2;

public sealed record Ps2BootInfo(
    string VolumeIdentifier,
    string BootKey,
    string BootPath,
    string ExecutableIsoPath,
    string ExecutableName,
    long ExecutableSize,
    long ExecutableEntryPoint,
    int ExecutableProgramHeaderCount,
    string? Serial,
    IReadOnlyDictionary<string, string> Config);

public sealed record Ps2Disc(
    Iso9660Filesystem Filesystem,
    Ps2BootInfo Boot,
    Iso9660ExtentReader BootExecutable,
    Elf32Header BootExecutableHeader);

/// <summary>
/// Identify a PS2 disc from bounded ISO-9660 metadata + SYSTEM.CNF + the fixed
/// ELF32 boot header. Mirrors <c>reference-ts/packages/ps2-disc</c>.
/// </summary>
public static partial class Ps2Boot
{
    private const int DefaultMaxSystemCnfBytes = 64 * 1024;

    public static Ps2Disc OpenDisc(IRandomAccessReader reader, int maxSystemCnfBytes = DefaultMaxSystemCnfBytes)
    {
        var fs = Iso9660Filesystem.Open(reader);
        var root = fs.List("/") ?? throw new InvalidDataException("ISO-9660 root directory was not found.");

        var cnfEntry = root.FirstOrDefault(e => !e.IsDirectory && e.Name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("PS2 SYSTEM.CNF was not found in the ISO-9660 root directory.");
        if (cnfEntry.DataLength > maxSystemCnfBytes)
        {
            throw new InvalidDataException($"PS2 SYSTEM.CNF is unexpectedly large: {cnfEntry.DataLength} bytes.");
        }

        var cnfReader = new Iso9660ExtentReader(reader, cnfEntry, fs.Volume.LogicalBlockSize, "SYSTEM.CNF");
        var cnfBytes = cnfReader.Read(0, (int)cnfReader.Length);
        var config = ParseSystemCnf(Ascii(cnfBytes).TrimEnd('\0'));

        string? bootKey = config.ContainsKey("BOOT2") ? "BOOT2" : config.ContainsKey("BOOT") ? "BOOT" : null;
        if (bootKey is null)
        {
            throw new InvalidDataException("PS2 SYSTEM.CNF does not contain BOOT2 or BOOT.");
        }

        string bootPath = config[bootKey];
        if (bootPath.Length == 0)
        {
            throw new InvalidDataException($"PS2 SYSTEM.CNF {bootKey} value is empty.");
        }

        string executableIsoPath = BootPathToIso9660Path(bootPath);
        string executableName = ExecutableNameFromBootPath(bootPath);
        string? serial = ParseExecutableSerial(executableName);

        var bootExecutable = fs.OpenFile(executableIsoPath)
            ?? throw new InvalidDataException($"PS2 boot executable '{executableIsoPath}' referenced by {bootKey} was not found in the ISO-9660 filesystem.");

        var header = Elf32Reader.ReadHeader(bootExecutable);
        ValidateBootElfHeader(header);

        var boot = new Ps2BootInfo(
            fs.Volume.VolumeIdentifier,
            bootKey,
            bootPath,
            executableIsoPath,
            executableName,
            bootExecutable.Length,
            header.Entry,
            header.ProgramHeaderCount,
            serial,
            config);

        return new Ps2Disc(fs, boot, bootExecutable, header);
    }

    public static Ps2BootInfo ReadBootInfo(IRandomAccessReader reader, int maxSystemCnfBytes = DefaultMaxSystemCnfBytes) =>
        OpenDisc(reader, maxSystemCnfBytes).Boot;

    public static IReadOnlyList<Elf32ProgramHeader> ReadBootProgramHeaders(Ps2Disc disc) =>
        Elf32Reader.ReadProgramHeaders(disc.BootExecutable, disc.BootExecutableHeader);

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex CnfLine();

    public static IReadOnlyDictionary<string, string> ParseSystemCnf(string text)
    {
        var config = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var m = CnfLine().Match(line.TrimEnd('\r'));
            if (m.Success)
            {
                config[m.Groups[1].Value.ToUpperInvariant()] = m.Groups[2].Value;
            }
        }

        return config;
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9]*):")]
    private static partial Regex DevicePrefix();

    public static string BootPathToIso9660Path(string bootPath)
    {
        string cleaned = bootPath.Trim().Trim('\'', '"');
        var device = DevicePrefix().Match(cleaned);
        string path = cleaned;
        if (device.Success)
        {
            if (!device.Groups[1].Value.Equals("cdrom0", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsupported PS2 boot device '{device.Groups[1].Value}:'.");
            }

            path = cleaned[device.Length..];
        }

        var segments = Iso9660Filesystem.NormalizePath(path);
        if (segments.Count == 0)
        {
            throw new InvalidDataException("PS2 boot path does not name an ISO-9660 file.");
        }

        return string.Join('/', segments);
    }

    public static string ExecutableNameFromBootPath(string bootPath)
    {
        string cleaned = bootPath.Trim().Trim('\'', '"');
        string leaf = cleaned.Split('\\', '/').LastOrDefault() ?? cleaned;
        return Regex.Replace(leaf, @";[0-9]+$", "", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"(?:^|[^A-Z0-9])([A-Z]{4})[_-]([0-9]{3})\.([0-9]{2})(?:$|[^A-Z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SerialPattern();

    /// <summary>SCUS_971.99 → SCUS-97199.</summary>
    public static string? ParseExecutableSerial(string executableName)
    {
        var m = SerialPattern().Match(executableName);
        return m.Success ? $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}{m.Groups[3].Value}" : null;
    }

    private static void ValidateBootElfHeader(Elf32Header header)
    {
        if (header.Endian != "little")
        {
            throw new InvalidDataException($"PS2 boot executable must be little-endian ELF32, got {header.Endian}-endian.");
        }

        if (header.Type != Elf32Reader.TypeExecutable)
        {
            throw new InvalidDataException($"PS2 boot executable ELF type {header.Type} is not ET_EXEC ({Elf32Reader.TypeExecutable}).");
        }

        if (header.Machine != Elf32Reader.MachineMips)
        {
            throw new InvalidDataException($"PS2 boot executable machine {header.Machine} is not MIPS ({Elf32Reader.MachineMips}).");
        }
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = bytes.Length <= 1024 ? stackalloc char[bytes.Length] : new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        return new string(chars);
    }
}
