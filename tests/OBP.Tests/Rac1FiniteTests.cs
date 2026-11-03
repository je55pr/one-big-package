using System.Numerics;
using OBP.RAC1;
using OBP.RAC1.Gameplay;

namespace OBP.Tests;

public sealed class Rac1FiniteTests
{
    [Fact]
    public void Vector3AdmitsOnlyFullyFiniteComponents()
    {
        Assert.True(Rac1Finite.Finite(new Vector3(1f, -2f, 3f)));
        Assert.True(Rac1Finite.Finite(Vector3.Zero));
        Assert.False(Rac1Finite.Finite(new Vector3(float.NaN, 0f, 0f)));
        Assert.False(Rac1Finite.Finite(new Vector3(0f, float.PositiveInfinity, 0f)));
        Assert.False(Rac1Finite.Finite(new Vector3(0f, 0f, float.NegativeInfinity)));
    }

    [Fact]
    public void TaxiPointAdmitsOnlyFullyFiniteCoordinates()
    {
        Assert.True(Rac1Finite.Finite(new Rac1TaxiPoint(1, -2, 3)));
        Assert.False(Rac1Finite.Finite(new Rac1TaxiPoint(double.NaN, 0, 0)));
        Assert.False(Rac1Finite.Finite(new Rac1TaxiPoint(0, double.PositiveInfinity, 0)));
        Assert.False(Rac1Finite.Finite(new Rac1TaxiPoint(0, 0, double.NegativeInfinity)));
    }
}
