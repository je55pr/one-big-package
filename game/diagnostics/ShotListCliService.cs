using OBP.RAC2;
using OBP.Core;
using OBP.Runtime.Presentation;

namespace OBP.Game.Diagnostics;

public interface IShotListCaptureFixture
{
    bool OpenWorld(string destinationId);
    void Apply(ShotSpec shot);
    IDictionary<string, object?> ReadMetadata();
    Task<bool> CaptureAsync(string output, int settleFrames, Func<IDictionary<string, object?>> metadata, CancellationToken token);
}

/// <summary>The explicit --shots workflow. The renderer owns camera bindings and the post-draw capture primitive.</summary>
public sealed class ShotListCliService(CommandLineArgs args, Action<ObpSourceGame, string> registerSource, IShotListCaptureFixture fixture,
    Action<string, int> quit, Action<string> log, Action<string> error)
{
    public async Task RunShotsAsync(string listPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShotList list;
        try { list = ShotList.Parse(File.ReadAllText(listPath)); }
        catch (Exception ex)
        {
            error($"[shots] could not read shot list '{listPath}': {ex.Message}");
            quit("shots-list-read-failed", 2);
            return;
        }
        RegisterSources();
        string? token = args.ShotsWorld ?? list.World ?? args.Planet;
        string destination, slug;
        if (token is { } value && value.Contains(':'))
        {
            destination = value;
            slug = value.Replace(':', '-').ToLowerInvariant();
        }
        else
        {
            int level = token is null ? args.Rac2Level : Rac2PlanetCatalogue.Resolve(token) ?? args.Rac2Level;
            if (args.Rac2Iso is null)
            {
                error("[shots] a GC planet token needs --rac2-iso");
                quit("shots-missing-gc-source", 2);
                return;
            }
            destination = $"rac2:LEVEL{level}";
            slug = (Rac2PlanetCatalogue.Find(level)?.Planet ?? $"level{level}").ToLowerInvariant().Replace(' ', '-');
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!fixture.OpenWorld(destination))
        {
            error($"[shots] world '{token ?? "?"}' did not load");
            quit("shots-world-load-failed", 1);
            return;
        }
        int failures = 0;
        for (int i = 0; i < list.Shots.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shot = list.Shots[i];
            log($"[shots] {i + 1}/{list.Shots.Count}  {shot.Name}  camera={shot.Camera} overlay={shot.Overlay ?? "-"}");
            fixture.Apply(shot);
            string output = Path.Combine(args.ShotsOut ?? Path.Combine("captures", "shots"), $"{slug}-{shot.Name}.png");
            bool saved = await fixture.CaptureAsync(output, shot.SettleFrames, () =>
            {
                var metadata = fixture.ReadMetadata();
                metadata["capture"] = $"shot:{shot.Name}";
                metadata["shot"] = new Dictionary<string, object?>
                {
                    ["name"] = shot.Name,
                    ["camera"] = shot.Camera.ToString(),
                    ["overlay"] = shot.Overlay,
                    ["settleFrames"] = shot.SettleFrames,
                };
                return metadata;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!saved) failures++;
        }
        log($"[shots] done — {list.Shots.Count - failures}/{list.Shots.Count} written to captures/shots/");
        quit(failures == 0 ? "shots-complete" : "shots-capture-failed", failures == 0 ? 0 : 1);
    }
    private void RegisterSources()
    {
        if (args.Rac1Iso is { } rac1) registerSource(ObpSourceGame.Rac1, rac1);
        if (args.Rac2Iso is { } rac2) registerSource(ObpSourceGame.Rac2, rac2);
        if (args.Rac3Iso is { } rac3) registerSource(ObpSourceGame.Rac3, rac3);
    }
}
