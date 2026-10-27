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
        foreach (var actor in _actorWorld.Views.Values)
        {
            string adapter = actor.Registration.AdapterId;
            var target = actor.Registration.Instance.Target;
            bool admitted = adapter is "rac1/class749/v1" or "rac2/class2827/v1" or "rac3/class500/v1" ||
                adapter == "source-world/rac1" && target.NativeClassId is 500 or 501 or 511 or 749 or 1440 ||
                adapter == "source-world/rac2" && target.NativeClassId == 2827 || adapter == "source-world/rac3" && target.NativeClassId == 500;
            if (!admitted) throw new InvalidOperationException($"Capture is unavailable for native actor adapter {adapter}, class {target.NativeClassId}; no deterministic fixture is admitted.");
            adapters.Add(adapter);
        }
        return adapters.Select(id => new PlaytestAdapter(id, 1, 60)).ToArray();
    }

    private bool CaptureWorldReady()
    {
        if (_fusionPreparation is not null || _actorWorld.Preparation.Values.Any(task => !task.IsCompleted)) return false;
        if (EffectCaptureContract.Includes(_args, _playtest?.Reader?.Header))
        {
            var resources = _worldScenes?.Current?.Resources;
            if (resources?.EffectsFailure is { } setupFailure) throw new InvalidDataException(setupFailure);
            var effects = resources?.Effects; effects?.Prepare();
            if (effects?.Failure is { } failure) throw new InvalidDataException(failure);
            if (effects?.Ready != true) return false;
        }
        if (_actorWorld.Problems.Count > 0) throw new InvalidOperationException("Capture requires admitted actor contexts: " + string.Join("; ", _actorWorld.Problems.Values));
        if (_actorWorld.Lab is not null && _actorWorld.Placed.Count != _actorWorld.Placements.Count) return false;
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
            "equip" => SelectPlayerEquipment(command.Value).Accepted,
            "gadget" => SetPlayerGadgetEnabled(command.Value, command.Enabled).Accepted,
            "quick-slot" => AssignPlayerQuickSelect(command.Slot, command.Value.Length == 0 ? null : command.Value).Accepted,
            "unequip" => UnequipPlayerEquipment().Accepted,
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
        foreach (var actor in _actorWorld.Views.Values.OrderBy(actor => actor.Registration.Instance.Target.SourceGame)
            .ThenBy(actor => actor.Registration.Instance.Target.NativeClassId).ThenBy(actor => actor.Registration.Instance.Target.InstanceIndex))
        {
            var key = actor.Registration.Instance.Target; var state = actor.Observe();
            values.Add($"actor.{SourceIdentityWire.FormatSource(key.SourceGame)}.{key.NativeClassId}.{key.InstanceIndex}", new([state.Position.X, state.Position.Y, state.Position.Z,
                state.Alive ? 1 : 0, state.Retired ? 1 : 0, state.Health ?? -1, state.NativeState, state.NativeSequence ?? -1], Tolerance: .0001));
        }
        values.Add("obp.actor-clock", new([_actors?.TickCount ?? 0]));
        if (EffectCaptureContract.Includes(_args, _playtest?.Reader?.Header))
            _worldScenes?.Current?.Resources.Effects?.AddCheckpoint(values);
        static string Digest(object value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    }
}
