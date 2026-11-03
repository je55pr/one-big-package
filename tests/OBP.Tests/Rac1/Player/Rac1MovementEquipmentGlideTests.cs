using OBP.RAC1.Player;

namespace OBP.Tests;

public sealed class Rac1MovementEquipmentGlideTests
{
    [Theory]
    [InlineData(2, 3f)]
    [InlineData(3, 5f)]
    public void GlideDescendsAtPackSpecificRateEvenWithNeutralStick(int pack, float baseSpeed)
    {
        float expected = -(baseSpeed * (1f / 60f) * 0.72f);
        var neutral = Rac1MovementEquipmentGlide.Step(pack, 0, 0);
        var active = Rac1MovementEquipmentGlide.Step(pack, 0, 1);
        Assert.Equal(expected, neutral.VerticalStep);
        Assert.Equal(expected, active.VerticalStep);
        Assert.Equal(0, neutral.PlanarScalar);
        Assert.Equal(15f / 3600f, active.PlanarScalar);
    }

    [Fact]
    public void PlanarAccelerationAndReleaseUseDifferentRatesAndDoNotOvershoot()
    {
        float speed = 0;
        for (int i = 0; i < 100; i++)
            speed = Rac1MovementEquipmentGlide.Step(2, speed, 1).PlanarScalar;
        Assert.Equal(3f * (1f / 60f), speed);
        float released = Rac1MovementEquipmentGlide.Step(2, speed, 0).PlanarScalar;
        Assert.Equal(speed - (7f * (1f / 3600f)), released);
        Assert.Equal(0, Rac1MovementEquipmentGlide.Step(2, 0.0001f, 0).PlanarScalar);
        float half = (3f * (1f / 60f)) * 0.5f;
        Assert.Equal(half, Rac1MovementEquipmentGlide.Step(2, half, 0.5f).PlanarScalar);
    }

    [Fact]
    public void AdmissionUsesStrictAgeAndClearanceAndExcludesHydroAndFlipFamilies()
    {
        bool Admit(int pack = 2, int age = 31, float clearance = 1.01f,
            bool available = true, bool disabled = false, bool held = true, int action = 7) =>
            Rac1MovementEquipmentGlide.CanEnter(pack, available, disabled, held, age, 30, clearance, action,
                recentCrossPress: true);
        Assert.True(Admit());
        Assert.True(Admit(pack: 3));
        Assert.False(Admit(pack: 4));
        Assert.False(Admit(age: 30));
        Assert.False(Admit(clearance: 1f));
        Assert.False(Admit(available: false));
        Assert.False(Admit(disabled: true));
        Assert.False(Admit(held: false));
        foreach (int excluded in new[] { 0x0b, 0x11, 0x12, 0x1c, 0x3c, 0x69 })
            Assert.False(Admit(action: excluded));
        Assert.False(Admit(action: 0x0e, clearance: 1.5f));
        Assert.True(Admit(action: 0x0e, clearance: MathF.BitIncrement(1.5f)));
    }

    [Fact]
    public void FirstJumpHoldDoesNotBecomeGlideButRecentSecondCrossAndHighPackExceptionsCan()
    {
        bool Admit(int action, bool recentCross = false) =>
            Rac1MovementEquipmentGlide.CanEnter(2, true, false, true, 90, 0, 5, action, recentCross);
        foreach (int action in new[] { 7, 9, 0x0a, 0x10 })
        {
            Assert.False(Admit(action));
            Assert.True(Admit(action, true));
        }
        foreach (int action in new[] { 0x0d, 0x0e, 0x0f })
            Assert.True(Admit(action));
    }

    [Fact]
    public void GlideEntryScalesScalarByTheFullContactVectorLength()
    {
        float limit = Rac1MovementEquipmentGlide.EntryMaximumControlledMotion;
        Assert.Equal(0.095f, Rac1MovementEquipmentGlide.EnterScalar(0.095f, limit));
        Assert.Equal(0.095f, Rac1MovementEquipmentGlide.EnterScalar(0.095f, 0));
        Assert.Equal(0.0475f, Rac1MovementEquipmentGlide.EnterScalar(0.095f, limit * 2));
        float contact3DLength = MathF.Sqrt((0.095f * 0.095f) + (0.2f * 0.2f));
        Assert.Equal(0.095f * (limit / contact3DLength),
            Rac1MovementEquipmentGlide.EnterScalar(0.095f, contact3DLength));
        Assert.True(Rac1MovementEquipmentGlide.EnterScalar(0.095f, contact3DLength) < limit);
    }

    [Fact]
    public void RejectsUnboundedOrNonFiniteInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1MovementEquipmentGlide.Step(4, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1MovementEquipmentGlide.Step(2, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1MovementEquipmentGlide.Step(2, 0, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rac1MovementEquipmentGlide.Step(2, 0, 1.1f));
    }
}
