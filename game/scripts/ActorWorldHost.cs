using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Only the active world's registrations, jobs awaiting activation and presentation transients.</summary>
internal sealed class ActorWorldHost
{
    public ActorRuntimeSet? Runtime { get; private set; }
    public FusionLabDefinition? Lab { get; private set; }
    public Dictionary<WeaponTargetKey, HostedActor> Views { get; } = [];
    public Dictionary<string, ActorPlacement> Placements { get; } = [];
    public Dictionary<string, ActorInstanceKey> Placed { get; } = [];
    public Dictionary<string, Task<RuntimeWorld>> Preparation { get; } = [];
    public Dictionary<string, ActorSourceVersion> Sources { get; } = [];
    public Dictionary<WeaponTargetKey, CharacterBody3D> Bodies { get; } = [];
    public Dictionary<WeaponTargetKey, HashSet<int>> Rewards { get; } = [];
    public Dictionary<WeaponTargetKey, RuntimeAudioHost> Audio { get; } = [];
    public Dictionary<string, string> Problems { get; } = [];
    public OBP.RAC1.Gameplay.Rac1PickupRandom? PlaybackRandom { get; set; }
    public ActorAdapterRegistry<IActorFactory>? Factories { get; set; }

    public void Begin(string world, long generation, FusionLabDefinition? lab)
    {
        if (Runtime is not null) throw new InvalidOperationException("Actors already belong to a live world lease.");
        Runtime = new(world, generation); Lab = lab;
    }

    public void Reset()
    {
        var runtime = Runtime;
        try { runtime?.Dispose(); }
        finally
        {
            Runtime = null;
            foreach (var audio in Audio.Values) audio.StopAll();
            foreach (var body in Bodies.Values.Where(GodotObject.IsInstanceValid)) body.CollisionLayer = body.CollisionMask = 0;
            Audio.Clear(); Rewards.Clear(); PlaybackRandom = null; Bodies.Clear();
            Preparation.Clear(); Sources.Clear(); Views.Clear(); Placements.Clear(); Placed.Clear(); Problems.Clear();
            Lab = null; Factories = null;
            foreach (var error in runtime?.RetirementFailures ?? Array.Empty<Exception>())
                GD.PrintErr("[actors] retirement failed after lease cleanup: " + error);
        }
    }
}
