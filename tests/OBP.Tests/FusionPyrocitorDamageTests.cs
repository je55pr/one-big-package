using OBP.Core;
using OBP.Game;
using OBP.RAC1.Gameplay;
using OBP.Runtime.Gameplay;

namespace OBP.Tests;

public sealed class FusionPyrocitorDamageTests
{
    [Fact]
    public void LabRac1TargetReceivesNativeClass176DamageNotAForeignFlagTranslation()
    {
        var target = new WeaponTargetKey(ObpSourceGame.Rac1, Rac1ClassIds.VeldinToad, 7);
        var key = new SourceWeaponKey(ObpSourceGame.Rac1, (int)Rac1WeaponId.Pyrocitor);
        var intent = new WeaponDamageIntent(new(key, 19, 0), target,
            (float)Rac1WeaponProfiles.PyrocitorDamage, Rac1WeaponProfiles.PyrocitorFlags, "native-flame");
        var damage = Assert.IsType<Rac1GameplayDamageEvent>(FusionDamagePolicy.Translate(intent));

        Assert.Equal(Rac1WeaponProfiles.PyrocitorDamage, damage.NativeDamage);
        Assert.Equal(Rac1WeaponProfiles.PyrocitorFlags, damage.NativeDamageFlags);
        Assert.Equal(new Rac1GameplayEntityRef(Rac1GameplayEntityKind.PlayerEquipment, 176, 16), damage.Source);
        Assert.Null(damage.HostWeaponIntent);
        Assert.True(Rac1EnemyDamage.IsPlayerWeapon(damage));

        Assert.Null(FusionDamagePolicy.Translate(intent with { SourceFlags = 0 }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { SourceDamage = 2 }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { Kind = "native-blast" }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { Shot = new(new(ObpSourceGame.Rac2, 16), 19, 0) }));
        Assert.Null(FusionDamagePolicy.Translate(intent with { Target = target with { SourceGame = ObpSourceGame.Rac3 } }));
    }
}
