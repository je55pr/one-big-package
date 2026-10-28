using System.Text.Json;
using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Diagnostics;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

public partial class OBPGame
{
    private IReadOnlyList<PlaytestAdapter> CaptureReplayAdapters()
    {
        var adapters = new SortedSet<string>(StringComparer.Ordinal) { "obp/shared-player/v1", "rac1/ordinary-player/v1", "obp/weapon-controllers/v1" };
        if (EffectCaptureContract.Includes(_args, _playtest?.Reader?.Header) && _worldScenes?.Current?.Resources.Effects is { } effects)
            adapters.Add(EffectCaptureContract.AdapterPrefix + effects.ReplayIdentity);
        foreach (string adapter in ActorHost.ReadReplayAdapters()) adapters.Add(adapter);
        return adapters.Select(id => new PlaytestAdapter(id, 1, 60)).ToArray();
    }

    private bool CaptureWorldReady()
    {
        var actors = ActorHost.ReadSnapshot();
        if (_fusionPreparation is not null || actors.Preparing) return false;
        if (EffectCaptureContract.Includes(_args, _playtest?.Reader?.Header))
        {
            var resources = _worldScenes?.Current?.Resources;
            if (resources?.EffectsFailure is { } setupFailure) throw new InvalidDataException(setupFailure);
            var effects = resources?.Effects; effects?.Prepare();
            if (effects?.Failure is { } failure) throw new InvalidDataException(failure);
            if (effects?.Ready != true) return false;
        }
        if (actors.Problems.Length > 0) throw new InvalidOperationException("Capture requires admitted actor contexts: " + string.Join("; ", actors.Problems.Select(problem => problem.Message)));
        if (actors.Lab && actors.Placed != actors.Expected) return false;
        _ = CaptureReplayAdapters();
        return _player is not null && _world is not null;
    }

    private void RecordCaptureCommand(PlaytestCommand command)
    {
        if (OrdinaryPlayerSession) _playtest?.RecordSessionCommand(command);
    }

    private void RecordGameplayFact(string kind, object facts)
    {
        if (OrdinaryPlayerSession) _playtest?.RecordGameplayFact(new(kind, _fusion?.Tick ?? 0, JsonSerializer.SerializeToElement(facts)));
    }

    private void ApplyCaptureCommand(PlaytestCommand command)
    {
        bool accepted = command.Kind switch
        {
            "destination" => command.Value == "obp:fusion-lab" ? EnterFusionLab() :
                TrilogyWorldProviders.Resolve(command.Value) is { } destination && OnDestinationChosen(destination),
            "equip" or "gadget" or "quick-slot" or "unequip" => _persistentPlayer.Equipment.ApplyCommand(command).Accepted,
            "effects" => _worldScenes?.Current?.Resources.Effects?.Control(command.Value).Accepted == true,
            _ => false,
        };
        if (!accepted) throw new InvalidDataException("Recorded session operation is unavailable: " + command.Kind + " " + command.Value);
    }

    private void ValidateCaptureProvenance(PlaytestRunHeader header)
    {
        if (header.Version == PlaytestRunHeader.LegacyVersion) return;
        header.VerifySessionSources(Sources.Setup.Library.Attached.Values.Select(source => source.Identity), CaptureReplayAdapters());
    }

    private void CaptureSharedCheckpoint(SortedDictionary<string, PlaytestValue> values)
    {
        if (!OrdinaryPlayerSession) return;
        byte[] player = JsonSerializer.SerializeToUtf8Bytes(ProfilePlayer.Capture(_playerSession!.Capture()) with { Generation = 0 }, ObpProfile.Options);
        values.Add("obp.player", new(Text: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(player))));
        values.Add("obp.adapters", new(Text: Digest(CaptureReplayAdapters())));
        values.Add("obp.sources", new(Text: Digest(CaptureProfileSources())));
        foreach (var actor in ActorHost.ReadActors().OrderBy(actor => actor.Registration.Instance.Target.SourceGame)
            .ThenBy(actor => actor.Registration.Instance.Target.NativeClassId).ThenBy(actor => actor.Registration.Instance.Target.InstanceIndex))
        {
            var key = actor.Registration.Instance.Target; var state = actor.State;
            values.Add($"actor.{SourceIdentityWire.FormatSource(key.SourceGame)}.{key.NativeClassId}.{key.InstanceIndex}", new([state.Position.X, state.Position.Y, state.Position.Z,
                state.Alive ? 1 : 0, state.Retired ? 1 : 0, state.Health ?? -1, state.NativeState, state.NativeSequence ?? -1], Tolerance: .0001));
        }
        values.Add("obp.actor-clock", new([ActorHost.TickCount]));
        if (EffectCaptureContract.Includes(_args, _playtest?.Reader?.Header))
            _worldScenes?.Current?.Resources.Effects?.AddCheckpoint(values);
        static string Digest(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    }
}
