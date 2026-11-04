using Godot;
using OBP.Core;
using OBP.RAC1.Gameplay;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

internal sealed record FusionWeaponOptions(FusionWeaponSession? Persistent, bool LegacyReplayWithoutWeapons,
    bool IsolatedTest, int Seed, bool Ordinary, string? DestinationId, bool Recording, bool HidePreparation);

internal readonly record struct FusionWeaponPlayerSnapshot(bool Present, bool Loading, bool Alive,
    bool Scripted, bool WrenchActive, bool WrenchThrown, bool WaterHidden, bool QuickSelectOpen, bool TriggerHeld);

internal readonly record struct FusionWeaponPoseSnapshot(bool UseStarted, bool TriggerHeld);
internal readonly record struct FusionWeaponModelSnapshot(int Age, int Sequence);

internal sealed record FusionWeaponPlayerPorts(Func<FusionWeaponOptions> Options,
    Func<FusionWeaponPlayerSnapshot> Snapshot, Func<Rac1WeaponInventory> Inventory,
    Func<PlayerHeldEquipmentSession> Hand, Func<IReadOnlyList<EquipmentCatalogEntry>> Equipment,
    Action<Rac1WeaponId> SelectNative, Action HoldNative, Action<SourceWeaponKey> SelectionChanged,
    Action RefreshAvailability, Action RefreshHeldVisibility, Action RefreshHud,
    Action<bool> ClearNativeTransients, Func<RuntimeSpawn, Rac1FusionCampaignIsolation> IsolateCampaign,
    Action<bool> ReloadFixture, Action<string> Report, Action<string, object> Record);

internal sealed record FusionWeaponSourcePorts(Func<IEnumerable<ObpAttachedSource>> Attached,
    Action RefreshPlayerSources, Action InvalidateActors, Action BindPresentation);

internal sealed record FusionWeaponNativePresentationPorts(Func<int, string, Node3D> CreateEffect,
    Action<int, int, Vector3> PlaySound, Action<RuntimeWorld> Configure);

internal readonly record struct FusionWeaponTargetGeometry(WeaponTargetKey Key, Aabb Bounds, bool Active, bool RewardEmitted);

internal sealed record FusionWeaponContactPorts(Func<PlayerHost?> Player, Func<Vector3> Forward,
    Func<bool> Ordinary, Func<IEnumerable<FusionWeaponTargetGeometry>> Geometry,
    Func<WeaponQuery, Vector3, IEnumerable<WeaponContact>> TraceOrdinary,
    Func<WeaponDamageIntent, bool> Apply, Func<global::Godot.Collections.Array<Rid>> Exclusions,
    Func<Vector3, Vector3, bool> LineOfSight);
