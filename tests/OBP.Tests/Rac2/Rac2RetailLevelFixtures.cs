using OBP.IO;
using OBP.PS2.Iso;
using OBP.RAC2;
using OBP.RAC2.Level;
using OBP.Runtime;

namespace OBP.Tests;

/// <summary>Shared access to the retail Going Commando level WADs for the <c>Rac2Level*Tests</c> classes.</summary>
internal static class Rac2RetailLevelFixtures
{
    /// <summary>Returns the <c>OBP_GC_ISO</c> path, or skips the calling test when it is not set.</summary>
    internal static string RequireIso()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        Skip.If(string.IsNullOrEmpty(iso), "OBP_GC_ISO not set");
        return iso!;
    }

    internal static (IRandomAccessReader wad, Rac2LevelWad.Header header) OpenLevelWad(string iso, int level)
    {
        var reader = new FileRandomAccessReader(iso);
        var fs = Iso9660Filesystem.Open(reader);
        var wad = fs.OpenFile($"/G/LEVEL{level}.WAD") ?? throw new FileNotFoundException($"/G/LEVEL{level}.WAD");
        return (wad, Rac2LevelWad.ReadHeader(wad));
    }
}

/// <summary>
/// Imports the retail Oozla (LEVEL1) world once per test class, on first use. The tests only read the shared world, so
/// one import replaces the one-per-test import the single large world-import test used to perform. The import is lazy so
/// that a LEVEL1 import failure fails only the tests that read the Oozla world, not the whole class.
/// </summary>
public sealed class Rac2OozlaWorldImportFixture : IDisposable
{
    private readonly Lazy<(FileRandomAccessReader? Reader, RuntimeWorld? World)> _import = new(Import);

    /// <summary>The imported Oozla world, or <see langword="null"/> when <c>OBP_GC_ISO</c> is not set.</summary>
    public RuntimeWorld? World => _import.Value.World;

    public void Dispose()
    {
        if (_import.IsValueCreated)
        {
            _import.Value.Reader?.Dispose();
        }
    }

    private static (FileRandomAccessReader? Reader, RuntimeWorld? World) Import()
    {
        var iso = Environment.GetEnvironmentVariable("OBP_GC_ISO");
        if (string.IsNullOrEmpty(iso))
        {
            return (null, null);
        }

        var reader = new FileRandomAccessReader(iso);
        try
        {
            return (reader, Rac2WorldImport.Build(reader, 1));
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}
