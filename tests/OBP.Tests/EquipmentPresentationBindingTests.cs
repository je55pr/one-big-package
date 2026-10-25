using OBP.Core;
using OBP.Fusion;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Tests;

public sealed class EquipmentPresentationBindingTests
{
    private static readonly EquipmentDefinition Item = new("gc/lancer", "Lancer", "rac2", "gc-build", EquipmentCategory.Weapon,
        EquipmentImplementation.Implemented, new("rac2", 30));
    private static readonly PlayerAvatarIdentity Avatar = new(ObpSourceGame.Rac1, "r1-build", "ratchet", "real-model");
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string> { ["rac2"] = "gc-build" };
    private static readonly IReadOnlySet<string> Sockets = new HashSet<string> { "measured-hand" };

    [Fact]
    public void BindingUsesItsOwnSourceBuildAndMeasuredAvatarSocket()
    {
        var binding = EquipmentPresentationBinding.Resolve(Item, Avatar, "moby:1490", "measured-hand", Sources, Sockets, true, true);
        Assert.True(binding.Ready); Assert.Equal(Item.Item, binding.Item);
        Assert.Equal("rac2", binding.Asset.Game); Assert.Equal("gc-build", binding.Asset.Build);
        Assert.Same(Avatar, binding.Avatar);
    }

    [Fact]
    public void EqualNativeItemNumberOrAnAvailableModelDoesNotGrantAnotherRigOrSource()
    {
        var wrongSource = EquipmentPresentationBinding.Resolve(Item with { SourceGame = "rac3", Item = new("rac3", 30) }, Avatar, "model", "measured-hand", Sources, Sockets, true, true);
        Assert.Equal(CapabilityAdmission.MissingSource, wrongSource.Admission);
        Assert.False(EquipmentPresentationBinding.Resolve(Item, Avatar, "model", "unknown-hand", Sources, Sockets, true, true).Ready);
        Assert.Equal(CapabilityAdmission.MissingContext, EquipmentPresentationBinding.Resolve(Item, Avatar, "model", "measured-hand", Sources, Sockets, true, false).Admission);
        Assert.Equal(CapabilityAdmission.MissingAssets, EquipmentPresentationBinding.Resolve(Item, Avatar, "model", "measured-hand", Sources, Sockets, false, true).Admission);
    }
}
