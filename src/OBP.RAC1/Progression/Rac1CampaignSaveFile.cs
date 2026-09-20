using System.Text.Json;

namespace OBP.RAC1.Progression;

/// <summary>
/// OBP host-file persistence for the recovered R&C1 campaign payload. JSON is an
/// OBP container only; native campaign meaning remains owned by
/// <see cref="Rac1CampaignSavePolicy"/> and <see cref="Rac1CampaignState"/>.
/// </summary>
public static class Rac1CampaignSaveFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static Rac1CampaignRestoreResult LoadOrDefault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Campaign save path is required.", nameof(path));

        if (!File.Exists(path))
            return Rac1CampaignSavePolicy.RestoreOrDefault(null);

        using FileStream stream = File.OpenRead(path);
        Rac1CampaignSaveEnvelope? envelope =
            JsonSerializer.Deserialize<Rac1CampaignSaveEnvelope>(stream, JsonOptions);
        if (envelope is null)
            throw new InvalidDataException("R&C1 campaign save contained no envelope.");

        return Rac1CampaignSavePolicy.RestoreOrDefault(envelope);
    }

    public static void Save(string path, Rac1CampaignState campaign)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Campaign save path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(campaign);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = fullPath + ".tmp";
        try
        {
            Rac1CampaignSaveEnvelope envelope = Rac1CampaignSavePolicy.Capture(campaign);
            using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, envelope, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
