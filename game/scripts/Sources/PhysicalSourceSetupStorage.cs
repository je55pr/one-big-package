using OBP.IO;
using OBP.Core;
using OBP.PS2;

namespace OBP.Game;

internal sealed class PhysicalSourceSetupStorage : ISourceSetupStorage
{
    public bool Exists(string path) => File.Exists(path);
    public DocumentRead<ObpSavedSource[]> ReadConfiguration(string path) => ObpSourceLibrary.ReadConfig(path);
    public void SaveConfiguration(ObpSourceLibrary library, string path) => library.SaveConfig(path);
    public IRandomAccessReader Open(string path) => new FileRandomAccessReader(path);
    public SourceFileIdentity Inspect(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The configured disc image is missing or inaccessible.");
        using var access = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new(file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
    }
}
