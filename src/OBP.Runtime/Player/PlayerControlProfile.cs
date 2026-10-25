namespace OBP.Runtime.Player;

[Flags]
public enum PlayerActionCapabilities { None = 0, Wrench = 1, Equipment = 2 }

[Flags]
public enum PlayerInterfaceCapabilities { None = 0, QuickSelect = 1, EquipmentDevelopment = 2, MovementBookmarks = 4, RetailPresentation = 8 }

public sealed record PlayerInputPolicy(bool AlternateCrouch);

/// <summary>Resolved contact envelope supplied by a registered source adapter; distances are scene units.</summary>
public sealed record PlayerColliderProfile(float Radius, float Height, float FloorSnap, float MaximumFloorAngle, float SafeMargin)
{
    public void Validate()
    {
        if (!float.IsFinite(Radius) || Radius <= 0 || !float.IsFinite(Height) || Height < Radius * 2 ||
            !float.IsFinite(FloorSnap) || FloorSnap < 0 || !float.IsFinite(MaximumFloorAngle) || MaximumFloorAngle <= 0 ||
            !float.IsFinite(SafeMargin) || SafeMargin <= 0) throw new ArgumentException("Invalid player contact envelope.");
    }
}

/// <summary>Independent, admitted adapter bindings. The destination is not an input.</summary>
public sealed record PlayerControlProfile(SourceProfileKey Identity,
    string LocomotionAdapterId, string ContactAdapterId, string TraversalAdapterId, string CameraAdapterId,
    PlayerColliderProfile Collider, PlayerInputPolicy Input, PlayerActionCapabilities Actions, PlayerInterfaceCapabilities Interfaces)
{
    public void Validate()
    {
        Identity.Validate(); Collider.Validate();
        ArgumentNullException.ThrowIfNull(Input);
        if ((Actions & ~(PlayerActionCapabilities.Wrench | PlayerActionCapabilities.Equipment)) != 0 ||
            (Interfaces & ~(PlayerInterfaceCapabilities.QuickSelect | PlayerInterfaceCapabilities.EquipmentDevelopment | PlayerInterfaceCapabilities.MovementBookmarks | PlayerInterfaceCapabilities.RetailPresentation)) != 0 ||
            new[] { LocomotionAdapterId, ContactAdapterId, TraversalAdapterId, CameraAdapterId }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every player control channel needs an explicit adapter binding.");
    }
}

public sealed record PlayerAnimationProfile(SourceProfileKey Identity, string AdapterId, int NativeTicksPerSecond)
{
    public IReadOnlyList<PlayerAvatarIdentity> CompatibleAvatars { get; init; } = [];
}
