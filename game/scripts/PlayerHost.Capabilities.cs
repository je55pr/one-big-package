using OBP.Runtime.Player;

namespace OneBigPackage;

public partial class PlayerHost
{
    /// <summary>Admitted independent controller bindings; world origin never selects this profile.</summary>
    public PlayerControlProfile ControlProfile { get; set; } = PlayerHostProfiles.Diagnostic;
    public bool HasNativeLocomotion => ControlProfile.LocomotionAdapterId == PlayerHostProfiles.Locomotion;
    public bool HasNativeContacts => ControlProfile.ContactAdapterId == PlayerHostProfiles.Contact;
    public bool HasNativeTraversal => ControlProfile.TraversalAdapterId == PlayerHostProfiles.Traversal;
    public bool HasNativeCamera => ControlProfile.CameraAdapterId == PlayerHostProfiles.Camera;
    public bool CanWrenchAttack => ControlProfile.Actions.HasFlag(PlayerActionCapabilities.Wrench);
    public bool CanUseEquipment => ControlProfile.Actions.HasFlag(PlayerActionCapabilities.Equipment);
    public bool CanQuickSelect => ControlProfile.Interfaces.HasFlag(PlayerInterfaceCapabilities.QuickSelect);
    public bool CanDevelopEquipment => ControlProfile.Interfaces.HasFlag(PlayerInterfaceCapabilities.EquipmentDevelopment);
    public bool CanSaveMovementState => ControlProfile.Interfaces.HasFlag(PlayerInterfaceCapabilities.MovementBookmarks);
    public bool HasRetailPresentation => ControlProfile.Interfaces.HasFlag(PlayerInterfaceCapabilities.RetailPresentation);
}
