using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1AirborneHeliLongTests
{
    [Fact]
    public void HeliEntryRetainsCommonFivePointSevenTargetInsteadOfPrecedingActionFortyNineOverride()
    {
        Assert.Equal(5.7f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0a));
        Assert.Equal(5.7f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0b));
        Assert.Equal(3.5f * (1f / 60f), Rac1AirbornePlanarMotion.MaximumTargetStep(0x0e));
    }

    [Fact]
    public void HeliAnticipationUsesOldScalarThenAuthoredForwardRampAndRetainsTheResultAtLaunch()
    {
        var session = new Rac1AirborneHeliLongSession(0.08f, new(0.08f, 0));
        Assert.Equal(0.08f, session.Step(0, 1, 0).X);
        float previousScalar = session.Scalar;
        var ageOne = session.Step(1, 1, 0);
        Assert.Equal(previousScalar + ((1f / 3600f) * 53f), ageOne.X);
        for (int age = 2; age < 5; age++)
            session.Step(age, 1, 0);
        var authored = session.Request;
        float ramp = ((1f / 3600f) * 53f) * 4;
        Assert.Equal((5.7f * (1f / 60f)) + ramp, authored.X);
        Assert.True(authored.X > 0.15f);
        Assert.Equal(authored, session.Step(5, 0, MathF.PI));
        Assert.Equal(authored, session.Step(33, 0, -MathF.PI / 2, contactProcessedPlanarScalar: 0));
        Assert.Equal(0, session.Scalar);
    }

    [Fact]
    public void ReleasingInputDuringAnticipationChangesScalarButDoesNotRemoveAuthoredForwardTerm()
    {
        var session = new Rac1AirborneHeliLongSession(0.08f, new(0.08f, 0));
        session.Step(0, 0, 0);
        float oldScalar = 0.08f - (11f * (1f / 3600f));
        var step = session.Step(1, 0, 0);
        Assert.Equal(oldScalar + ((1f / 3600f) * 53f), step.X);
        Assert.Equal(oldScalar - (11f * (1f / 3600f)), session.Scalar);
    }
}
