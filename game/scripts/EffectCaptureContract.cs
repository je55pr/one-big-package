using OBP.Runtime.Diagnostics;

namespace OBP.Game;

/// <summary>Opt-in versioned effects provenance; older recordings keep their original adapter/checkpoint set.</summary>
public static class EffectCaptureContract
{
    public const string AdapterPrefix = "obp/source-effects/v1:";
    public static bool Includes(CommandLineArgs args, PlaytestRunHeader? replay) => replay is not null
        ? replay.Version == PlaytestRunHeader.CurrentVersion && replay.Adapters.Any(a => a.Id.StartsWith(AdapterPrefix, StringComparison.Ordinal))
        : args.RecordPlaytest is not null && !args.NativeParityRequested && (args.RecordEffects || args.EffectsSmoke);
}
