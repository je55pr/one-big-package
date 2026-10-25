using OBP.Core;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Fusion;

/// <summary>Admitted model/hand binding. Destination identity is deliberately absent.</summary>
public sealed record EquipmentPresentationBinding(SourceWeaponKey Item, FusionSourceIdentity Asset,
    PlayerAvatarIdentity Avatar, string Socket, CapabilityAdmission Admission, string? Reason)
{
    public bool Ready => Admission == CapabilityAdmission.Ready;

    public static EquipmentPresentationBinding Resolve(EquipmentDefinition item, PlayerAvatarIdentity avatar,
        string modelId, string socket, IReadOnlyDictionary<OBP.Core.ObpSourceGame, string> sources,
        IReadOnlySet<string> measuredSockets, bool modelAvailable, bool compatiblePose)
    {
        if (item.Item is not { } key || string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(socket))
            throw new ArgumentException("Presentation requires an existing item, model identity and measured socket.");
        var asset = new FusionSourceIdentity(item.SourceGame, item.BuildId, modelId);
        CapabilityAdmission admission; string? reason;
        if (item.Implementation != EquipmentImplementation.Implemented)
        { admission = CapabilityAdmission.Unimplemented; reason = item.UnsupportedReason; }
        else if (sources.GetValueOrDefault(item.SourceGame) != item.BuildId)
        { admission = CapabilityAdmission.MissingSource; reason = "The equipped item's original source/build is not attached."; }
        else if (!modelAvailable)
        { admission = CapabilityAdmission.MissingAssets; reason = "The source-native held model is not prepared."; }
        else if (!compatiblePose || !measuredSockets.Contains(socket))
        { admission = CapabilityAdmission.MissingContext; reason = "The selected avatar has no measured compatible pose/hand socket for this item."; }
        else { admission = CapabilityAdmission.Ready; reason = null; }
        return new(key, asset, avatar, socket, admission, reason);
    }
}
