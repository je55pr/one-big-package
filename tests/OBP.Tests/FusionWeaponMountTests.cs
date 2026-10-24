using System.Numerics;
using OneBigPackage;

namespace OBP.Tests;

public sealed class FusionWeaponMountTests
{
    [Fact]
    public void BothSourceBarrelsAlignWithTheGunSocketWithoutChangingDimensions()
    {
        foreach (var (weapon, up) in new[]
        {
            (FusionWeaponSession.Lancer, -Vector3.UnitX),
            (FusionWeaponSession.ShockCannon, Vector3.UnitY)
        })
        {
            var rotation = FusionWeaponMount.Rotation(weapon);
            Assert.True(Vector3.Distance(Vector3.Transform(-Vector3.UnitZ, rotation), -Vector3.UnitZ) < .00001f);
            Assert.True(Vector3.Distance(Vector3.Transform(up, rotation), -Vector3.UnitX) < .00001f);
            var a = new Vector3(.14f, -.25f, .6f);
            var b = new Vector3(-.3f, .1f, -.2f);
            Assert.True(MathF.Abs(Vector3.Distance(a, b) - Vector3.Distance(
                Vector3.Transform(a, rotation), Vector3.Transform(b, rotation))) < .00001f);
            var grip = FusionWeaponMount.Grip(weapon);
            var offset = -Vector3.Transform(grip, rotation);
            Assert.True((Vector3.Transform(grip, rotation) + offset).Length() < .00001f);
            var muzzle = Vector3.Transform(FusionWeaponMount.Muzzle(weapon), rotation) + offset;
            Assert.True(Vector3.Dot(muzzle, -Vector3.UnitZ) > .6f);
        }
    }
}
