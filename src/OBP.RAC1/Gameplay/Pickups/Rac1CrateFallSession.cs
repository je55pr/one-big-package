namespace OBP.RAC1.Gameplay;

/// <summary>
/// The unparented crate's vertical recurrence in 0x2d15fc..0x2d1668.
/// Host supplies support height; native platform attachment and crushing are separate.
/// </summary>
public sealed class Rac1CrateFallSession(float height)
{
    public const float GravityPerTick = 9.8f / 3600;
    public const float MaximumFallPerTick = .3f;
    public float Height { get; private set; } = height;
    public float Velocity { get; private set; }

    public void Step(float? supportHeight)
    {
        Velocity = Math.Clamp(Velocity - GravityPerTick, -MaximumFallPerTick, MaximumFallPerTick);
        float next = Height + Velocity;
        if (supportHeight is { } floor && next <= floor && floor <= Height + .05f)
        {
            Height = floor;
            Velocity = 0;
        }
        else Height = next;
    }
}
