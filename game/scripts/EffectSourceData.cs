using OBP.Core;
using OBP.Fusion;
using OBP.IO;
using OBP.RAC1;
using OBP.RAC1.Gameplay;
using OBP.RAC2;
using OBP.RAC2.Gameplay;
using OBP.RAC3;
using OBP.RAC3.Gameplay;
using OBP.Runtime.Effects;

namespace OBP.Game;

/// <summary>Verified, CPU-only recipe assets. The destination is deliberately absent from this loader.</summary>
internal sealed class EffectSourceData
{
    internal sealed record Stamp(string Path, long Length, long Written);
    public EffectRecipeRegistry Registry { get; } = new();
    public Dictionary<ObpSourceGame, Stamp> Stamps { get; } = [];
    public Dictionary<ObpSourceGame, RuntimeEffectProblem> Problems { get; } = [];
    public static EffectSourceData Load(IReadOnlyList<ObpAttachedSource> sources, FileVerificationCache cache,
        IProgress<string> progress, CancellationToken token)
    {
        var data = new EffectSourceData();
        Load(Rac1Authority.Primary, disc => Rac1ParticleEffectFactory.ReadNovalis(disc));
        Load(Rac2Authority.Primary, disc => Rac2ParticleEffectFactory.ReadMuseum(disc));
        Load(Rac3Authority.Primary, disc => Rac3ParticleEffectFactory.ReadVeldin(disc));
        return data;

        void Load(ObpBuildIdentity expected, Func<IRandomAccessReader, IEnumerable<IRuntimeEffectFactory>> decode)
        {
            token.ThrowIfCancellationRequested(); string name = SourceIdentityWire.FormatSource(expected.Game);
            var source = sources.SingleOrDefault(s => s.Game == expected.Game);
            if (source is null) { Unavailable(RuntimeEffectAvailability.MissingSource, $"Attach {name} NTSC-U in Game Sources."); return; }
            if (source.Identity.BuildId != expected.BuildId) { Unavailable(RuntimeEffectAvailability.WrongBuild, $"The {name} effect bank requires {expected.BuildId}."); return; }
            try
            {
                progress.Report($"Verifying {name} effect source…");
                var before = ReadStamp(source.Path);
                using var disc = new FileRandomAccessReader(source.Path);
                string hash = expected.Sha256 ?? throw new InvalidDataException("Missing authority hash.");
                var verification = cache.Verify(source.Path, disc, hash, cancellationToken: token);
                if (!string.Equals(verification.Sha256, hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source hash mismatch.");
                progress.Report($"Decoding {name} native effect bank…");
                var factories = decode(disc).ToArray(); token.ThrowIfCancellationRequested();
                if (before != ReadStamp(source.Path)) throw new InvalidDataException("Source changed during decoding.");
                data.Registry.Source(expected.Game, expected.BuildId);
                foreach (var factory in factories) data.Registry.Register(factory);
                data.Stamps.Add(expected.Game, before);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            { Unavailable(RuntimeEffectAvailability.MissingAssets, $"{name} native effect assets are unavailable ({error.GetType().Name}). Reattach the admitted original source."); }

            void Unavailable(RuntimeEffectAvailability kind, string reason)
            {
                var problem = new RuntimeEffectProblem(kind, reason); data.Problems[expected.Game] = problem;
                data.Registry.Source(expected.Game, expected.BuildId, problem);
            }
        }
    }
    public static Stamp ReadStamp(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("The effect source is no longer attached.");
        return new(path, file.Length, file.LastWriteTimeUtc.Ticks);
    }
}
