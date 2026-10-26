using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

internal sealed class HostedActor(ActorRegistration registration, RuntimeWorldScene.DynamicObjectNode node,
    Func<ActorObservation> observe, Func<WeaponDamageIntent, ActorHitResult> damage,
    Action<ActorTickFacts>? tick = null, Action? retire = null, IDisposable? resources = null) : IRegisteredActor
{
    public ActorRegistration Registration { get; } = registration;
    public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
    private bool _retired;
    public ActorObservation Observe() => _retired || !GodotObject.IsInstanceValid(Node.Root)
        ? new(System.Numerics.Vector3.Zero, false, true, null, 0, null) : observe();
    public void Tick(ActorTickFacts facts) { if (!_retired) tick?.Invoke(facts); }
    public ActorHitResult Apply(WeaponDamageIntent intent) => _retired ? new(false, true, "Actor was retired.") : damage(intent);
    public void Dispose()
    {
        if (_retired) return;
        _retired = true;
        try { retire?.Invoke(); }
        finally { resources?.Dispose(); }
    }
}
