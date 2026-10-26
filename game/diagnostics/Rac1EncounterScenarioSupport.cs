using System.Collections.Immutable;
using System.Numerics;

namespace OBP.Game.Diagnostics;

/// <summary>Source-qualified actor reads and the original bounded encounter wait phases.</summary>
public abstract class Rac1EncounterScenarioSupport(MovementDiagnosticContext context,
    IRac1EnemyDiagnosticFacts enemies, IRac1EnemyDiagnosticActions actions) : MovementScenarioSupport(context)
{
    protected IRac1EnemyDiagnosticFacts Enemies { get; } = enemies;
    protected IRac1EnemyDiagnosticActions Actors { get; } = actions;
    protected ImmutableArray<Rac1DiagnosticEnemy> ReadEnemies() =>
        [.. Enemies.ReadEnemies().Select(key => new Rac1DiagnosticEnemy(key, Enemies))];
    protected Rac1DiagnosticEnemy Enemy(int instance) => new(
        Enemies.ReadEnemies().Single(key => key.Target.InstanceIndex == instance), Enemies);
    protected static Vector3 ToEffectNative(Vector3 scene) => new(-scene.X, scene.Z, scene.Y);
    protected static Vector3 FromEffectNative(Vector3 native) => new(-native.X, native.Z, native.Y);
}
