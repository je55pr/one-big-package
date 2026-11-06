namespace OBP.RAC2.Gameplay;

/// <summary>Unparented crate recurrence at Oozla 0x3929B8..0x392CE4. Host supplies the static floor or live lower crate's top.</summary>
public sealed class Rac2CrateFallSession(float height)
{
    public static float GravityPerTick => BitConverter.Int32BitsToSingle(0x3B326750);
    public static float MaximumFallPerTick => .3f;
    public float Height { get; private set; } = height;
    public float Velocity { get; private set; }
    public void Step(float? support)
    {
        Velocity = Math.Clamp(Velocity - GravityPerTick, -MaximumFallPerTick, MaximumFallPerTick);
        float next = Height + Velocity;
        if (support is { } floor && next <= floor && floor <= Height + .05f)
        {
            Height = floor;
            Velocity = 0;
        }
        else Height = next;
    }
}
