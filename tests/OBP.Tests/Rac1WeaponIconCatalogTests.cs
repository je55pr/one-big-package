using OBP.RAC1.Gameplay;
using OBP.RAC1.Presentation;

namespace OBP.Tests;

public sealed class Rac1WeaponIconCatalogTests
{
    [Fact]
    public void EveryNamedOrdinaryQuickSelectWeaponHasTheNativeSprite()
    {
        var weapons = Enum.GetValues<Rac1WeaponId>()
            .Where(weapon => weapon != Rac1WeaponId.Wrench)
            .ToArray();

        Assert.Equal(15, weapons.Length);
        foreach (Rac1WeaponId weapon in weapons)
        {
            var icon = Rac1WeaponIconCatalog.Ordinary(weapon);
            Assert.True(icon.HasValue, $"Missing native quick-select icon for {weapon}");
            Assert.Equal(0xEA60 + (int)weapon, icon.Value.AnimationId);
            Assert.Equal(0, icon.Value.Frame);
        }
    }

    [Fact]
    public void WrenchAndUnidentifiedNativeSlotsDoNotClaimInventedIcons()
    {
        Assert.Null(Rac1WeaponIconCatalog.Ordinary(Rac1WeaponId.Wrench));
        Assert.Null(Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)12));
        Assert.Null(Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)22));
        Assert.Null(Rac1WeaponIconCatalog.Ordinary((Rac1WeaponId)36));
    }

    [Fact]
    public void MapIsInjectiveAcrossOrdinaryNamedWeapons()
    {
        var icons = Enum.GetValues<Rac1WeaponId>()
            .Select(Rac1WeaponIconCatalog.Ordinary)
            .Where(icon => icon.HasValue)
            .Select(icon => icon!.Value.AnimationId)
            .ToArray();

        Assert.Equal(icons.Length, icons.Distinct().Count());
    }
}
