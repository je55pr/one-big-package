using OBP.RAC1.Level;

namespace OBP.Tests;

public sealed class Rac1InstancesAdmissionTests
{
    [Theory]
    [InlineData(572)]
    [InlineData(865)]
    [InlineData(866)]
    [InlineData(623)]
    [InlineData(830)]
    public void AuthoredMobyLookupRequiresExactSourceIndexAndClass(int nativeClass)
    {
        var placement = new Rac1Instances.MobyInstance(0, nativeClass, 1,
            (1, 2, 3), (0, 0, 0), null, -1, new byte[Rac1Instances.MobyRecordSize], null);
        Rac1Instances.MobyInstance[] authored = [placement];

        Assert.Same(placement, Rac1Instances.RequirePlacement(authored, 0, nativeClass));
        Assert.Throws<InvalidDataException>(() => Rac1Instances.RequirePlacement(authored, -1, nativeClass));
        Assert.Throws<InvalidDataException>(() => Rac1Instances.RequirePlacement(authored, 1, nativeClass));
        Assert.Throws<InvalidDataException>(() => Rac1Instances.RequirePlacement(authored, 0, nativeClass + 1));

        authored[0] = placement with { Index = 4 };
        Assert.Throws<InvalidDataException>(() => Rac1Instances.RequirePlacement(authored, 0, nativeClass));
    }
}
