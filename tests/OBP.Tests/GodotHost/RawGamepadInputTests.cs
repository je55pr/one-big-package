using OBP.Godot.Controls;
using OBP.RAC1.Player;
using Xunit;

namespace OBP.Tests;

public sealed class RawGamepadInputTests
{
    [Fact]
    public void RawAxesPreserveComponentsAndUnclampedMagnitude()
    {
        var axes = new RawGamepadAxes(0.3f, -0.4f, 1f, 1f);

        Assert.Equal(0.3f, axes.LeftX);
        Assert.Equal(-0.4f, axes.LeftY);
        Assert.Equal(0.5f, axes.LeftMagnitude, 6);
        Assert.Equal(MathF.Sqrt(2f), axes.RightMagnitude, 6);
        Assert.True(axes.RightMagnitude > 1f);
    }

    [Fact]
    public void DirectionalStrengthCompositionAddsNoDeadZoneOrNormalization()
    {
        Assert.Equal(0.03125f, RawGamepadInputMath.ComposeAxis(0f, 0.03125f));
        Assert.Equal(0.5f, RawGamepadInputMath.ComposeAxis(0.125f, 0.625f));
        Assert.Equal(-0.75f, RawGamepadInputMath.ComposeAxis(0.75f, 0f));
    }

    [Fact]
    public void LogicalCardinalsRemainSymmetricAtFullAndPartialStrength()
    {
        var cardinals = new (float Left, float Right, float Forward, float Back)[]
        {
            (1f, 0f, 0f, 0f),
            (0f, 1f, 0f, 0f),
            (0f, 0f, 1f, 0f),
            (0f, 0f, 0f, 1f),
        };

        foreach (var cardinal in cardinals)
        {
            var full = Condition(cardinal, 1f);
            Assert.Equal(Rac1AnalogueSpeedBand.Run, full.SpeedBand);
            Assert.Equal(1d, full.Magnitude, 6);

            var partial = Condition(cardinal, 0.6f);
            Assert.Equal(Rac1AnalogueSpeedBand.Walk, partial.SpeedBand);
            Assert.InRange(partial.Magnitude, 0.37d, 0.39d);
        }

        static Rac1AnalogueInput.Conditioned Condition(
            (float Left, float Right, float Forward, float Back) cardinal,
            float strength)
        {
            float moveX = RawGamepadInputMath.ComposeAxis(
                cardinal.Left * strength,
                cardinal.Right * strength);
            float moveY = RawGamepadInputMath.ComposeAxis(
                cardinal.Forward * strength,
                cardinal.Back * strength);
            return Rac1AnalogueInput.ConditionUnitAxes(moveX, -moveY);
        }
    }

    [Fact]
    public void DiagnosticFormattingIsDeterministicAndMarksMissingGuid()
    {
        var diagnostic = new RawGamepadDiagnostic(
            7,
            "SDL Pad",
            string.Empty,
            true,
            new RawGamepadAxes(0.3f, -0.4f, 1f, 1f));

        Assert.Equal(
            "device=7 name=\"SDL Pad\" guid=\"<unavailable>\" known=true " +
            "left=(0.300000,-0.400000) |left|=0.500000 " +
            "right=(1.000000,1.000000) |right|=1.414214",
            diagnostic.Format());
    }
}
