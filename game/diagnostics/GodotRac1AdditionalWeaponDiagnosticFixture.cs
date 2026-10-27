using System.Collections.Immutable;
using Godot;
using OBP.RAC1.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game.Diagnostics;

internal static class GodotWeaponModelFacts
{
    internal static WeaponModelDiagnostic Read(Node3D node)
    {
        if (!GodotObject.IsInstanceValid(node)) return new(false, false, false, null, null, null, NVector3.Zero, NVector3.Zero);
        var meshes = node.GetChildren().OfType<MeshInstance3D>().ToArray(); var equipment = node as Rac1EquipmentView;
        return new(node.Visible, meshes.Length != 0, meshes.All(mesh => mesh.Mesh is ArrayMesh), equipment?.NativeClass,
            equipment?.Sequence, equipment?.FootOffset, GodotMovementPlayer.Copy(node.GlobalPosition), GodotMovementPlayer.Copy(node.Scale));
    }
}
internal sealed class GodotWeaponDiagnosticWitness<T>(GodotRac1DiagnosticWorldScope scope, Func<T> read) : IWeaponDiagnosticWitness<T>
{
    public T Read() { scope.RequireCurrent(); return read(); }
}
internal sealed class GodotRac1DecoyDiagnosticFixture(GodotRac1DiagnosticWorldScope scope, Func<Rac1DecoyDiagnostic> read, Action stageExpiry) : IRac1DecoyDiagnosticFixture
{
    public Rac1DecoyDiagnostic Read() { scope.RequireCurrent(); return read(); }
    public void StageExpiry() { scope.RequireCurrent(); stageExpiry(); }
}
internal sealed record GodotRac1AdditionalWeaponBindings(Func<Rac1AdditionalWeaponDiagnostic> Read,
    Func<Rac1WeaponId, Node3D?> Held, Func<Rac1WeaponId, Vector3> Muzzle, Func<int, int> Ammo,
    Func<int, int, bool> Audible, Func<ImmutableArray<Rac1WeaponAudioDiagnostic>> Sounds,
    Func<ImmutableArray<IWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>>> Shots,
    Func<ImmutableArray<IWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>>> Agents, Func<bool> Preview, Action<bool> SetPreviewDisabled);
internal sealed class GodotRac1AdditionalWeaponDiagnosticFixture(GodotRac1AdditionalWeaponBindings bindings) : IRac1AdditionalWeaponDiagnosticFixture
{
    public Rac1AdditionalWeaponDiagnostic Read() => bindings.Read();
    public WeaponModelDiagnostic ReadHeld(Rac1WeaponId weapon) => GodotWeaponModelFacts.Read(bindings.Held(weapon)
        ?? throw new DiagnosticUnavailableException("The original held weapon model is unavailable."));
    public NVector3 ReadMuzzle(Rac1WeaponId weapon) => GodotMovementPlayer.Copy(bindings.Muzzle(weapon));
    public int ReadAmmo(int nativeItem) => bindings.Ammo(nativeItem);
    public bool SourceVoiceAudible(int nativeClass, int sound) => bindings.Audible(nativeClass, sound);
    public ImmutableArray<Rac1WeaponAudioDiagnostic> ReadSoundTrace() => bindings.Sounds();
    public ImmutableArray<IWeaponDiagnosticWitness<Rac1EquipmentShotDiagnostic>> ReadShots() => bindings.Shots();
    public ImmutableArray<IWeaponDiagnosticWitness<Rac1DoomAgentDiagnostic>> ReadDoomAgents() => bindings.Agents();
    public IDisposable BeginPreview()
    {
        bool previous = bindings.Preview();
        var lease = new MovementFixtureLease(() => bindings.SetPreviewDisabled(!previous));
        try { bindings.SetPreviewDisabled(false); return lease; }
        catch (Exception error)
        {
            try { lease.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Weapon preview acquisition and restoration failed.", error, cleanup); }
            throw;
        }
    }
}
internal sealed record GodotRac1ArsenalBindings(Func<Rac1ArsenalDiagnostic> Read,
    Func<ImmutableArray<IWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>>> Missiles,
    Func<ImmutableArray<IRac1DecoyDiagnosticFixture>> Decoys, Func<ImmutableArray<Rac1ChickenDiagnostic>> Chickens,
    Action DiscardDrones, Action ResetWorldTick, Func<OBP.Fusion.ActorInstanceKey, NVector3, NVector3> EnemyGoal);
internal sealed class GodotRac1ArsenalDiagnosticFixture(GodotRac1ArsenalBindings bindings) : IRac1ArsenalDiagnosticFixture
{
    public Rac1ArsenalDiagnostic Read() => bindings.Read();
    public ImmutableArray<IWeaponDiagnosticWitness<Rac1ArsenalMissileDiagnostic>> ReadMissiles() => bindings.Missiles();
    public ImmutableArray<IRac1DecoyDiagnosticFixture> ReadDecoys() => bindings.Decoys();
    public ImmutableArray<Rac1ChickenDiagnostic> ReadChickens() => bindings.Chickens();
    public void DiscardDrones() => bindings.DiscardDrones();
    public void ResetWorldTick() => bindings.ResetWorldTick();
    public NVector3 ReadEnemyGoal(OBP.Fusion.ActorInstanceKey actor, NVector3 player) => bindings.EnemyGoal(actor, player);
}
