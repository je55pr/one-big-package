namespace OBP.RAC1.Camera;

/// <summary>Native option values: 0 = Reversed, 1 = Normal (menu table 0x001b5b40).</summary>
public enum Rac1CameraDirection
{
    Inverted = 0,
    Normal = 1,
}

public enum Rac1CameraSpeed
{
    Slow = 0,
    Medium = 1,
    Fast = 2,
}

/// <summary>
/// SCUS-97199 options at 0x0015ede0 / 0x0015eddc / 0x0015ede4.
/// The boot program initializes all three to 1. Applied after native stick conditioning.
/// </summary>
public sealed record Rac1CameraOptions(
    Rac1CameraDirection Horizontal = Rac1CameraDirection.Normal,
    Rac1CameraDirection Vertical = Rac1CameraDirection.Normal,
    Rac1CameraSpeed Speed = Rac1CameraSpeed.Medium)
{
    public static Rac1CameraOptions Default { get; } = new();

    /// <summary>0x002e9c28 selects one of the three radians/update values at 0x00161db0.</summary>
    public double YawRadiansPerUpdate => Speed switch
    {
        Rac1CameraSpeed.Slow => 0.01745329238474369d,
        Rac1CameraSpeed.Medium => 0.022689279168844223d,
        Rac1CameraSpeed.Fast => 0.027925267815589905d,
        _ => throw new ArgumentOutOfRangeException(nameof(Speed)),
    };

    public Rac1CameraOptions Validate()
    {
        if (!Enum.IsDefined(Horizontal) || !Enum.IsDefined(Vertical) || !Enum.IsDefined(Speed))
            throw new ArgumentOutOfRangeException(nameof(Rac1CameraOptions));
        return this;
    }

    /// <summary>0x002e89b0; caller supplies right-positive X and up-positive Y.</summary>
    public (double Yaw, double Pitch) SelectManualInput(double conditionedX, double conditionedYUp)
    {
        Validate();
        return (Horizontal == Rac1CameraDirection.Normal ? conditionedX : -conditionedX,
            Vertical == Rac1CameraDirection.Normal ? conditionedYUp : -conditionedYUp);
    }
}
