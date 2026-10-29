using OBP.RAC1.Gameplay;

namespace OBP.Game;

internal readonly record struct Rac1EnemyDamageAdmission(bool Lethal, int Sequence);

/// <summary>One admitted native family; the shared host supplies scene contact and presentation only.</summary>
internal interface IRac1EnemyAdapter
{
    int ClassId { get; }
    void Tick(Rac1HostedEnemy enemy);
    Rac1EnemyDamageAdmission Damage(Rac1GameplayDamageEvent damage);
}
