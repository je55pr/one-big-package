using Godot;
using OBP.Godot;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Audio;
using OBP.Runtime.Gameplay;

namespace OBP.Game;

/// <summary>Only host resources and contact/presentation inputs. Native reward authority stays in its source sessions.</summary>
internal sealed record Rac1LootPorts(
    Func<PlayerHost?> Player,
    Func<RuntimeWorld?> World,
    Func<Rac1WeaponInventory> Weapons,
    Func<Rac1RatchetNanotechSession> Nanotech,
    Func<Rac1BoltWallet> Wallet,
    Func<Rac1PickupRandom> Random,
    Func<bool> WrenchActive,
    Func<Vector3> WrenchPosition,
    Func<bool> Ordinary,
    Func<PlayerEquipmentPresentationBinder> Equipment,
    Func<string?> SourcePath,
    Func<RuntimeAudioPlaybackIntent, bool> PlayEffect,
    Action RetirePlayerAudio,
    Action<string> Persist,
    Action RefreshHud,
    Action<int> BoltFeedback,
    Action<int, int> RecordSound,
    Func<HashSet<int>> CollectedGoldBolts);

/// <summary>
/// One R&C1 world lease's transient pickup and collectible presentation.
/// Authored scene roots are borrowed, while spawned effects and contacts are owned.
/// </summary>
internal sealed partial class Rac1LootPresentation : IDisposable
{
    private readonly RuntimeWorldScene.Result _sceneResult;
    private readonly Rac1WorldContext _context;
    private readonly Rac1WorldPresentation _presentation;
    private readonly Rac1LootPorts _ports;
    private bool _disposed;

    internal Rac1LootPresentation(RuntimeWorldScene.Result scene, Rac1WorldContext context,
        Rac1WorldPresentation presentation, Rac1LootPorts ports)
    {
        _sceneResult = scene;
        _context = context;
        _presentation = presentation;
        _ports = ports;
    }

    private PlayerHost? _player => _ports.Player();
    private RuntimeWorld? _world => _ports.World();
    private Rac1WeaponInventory _rac1Weapons => _ports.Weapons();
    private Rac1RatchetNanotechSession _rac1Nanotech => _ports.Nanotech();
    private Rac1BoltWallet _rac1BoltWallet => _ports.Wallet();
    private PlayerEquipmentPresentationBinder _equipmentPresentation => _ports.Equipment();
    private bool OrdinaryPlayerSession => _ports.Ordinary();
    private Rac1PickupRandom Rac1EffectRandom => _ports.Random();
    private static bool IsInstanceValid(GodotObject? obj) => GodotObject.IsInstanceValid(obj);
    private void PersistRac1CampaignState(string reason) => _ports.Persist(reason);
    private void RefreshRac1HudState() => _ports.RefreshHud();
    private void RecordRac1WeaponSound(int nativeClass, int localSound) => _ports.RecordSound(nativeClass, localSound);
    private Vector3 WrenchWorldPosition() => _ports.WrenchPosition();

    internal Dictionary<int, Rac1BoltMotionSession> PickupMotions => _rac1PickupMotion;
    internal Dictionary<int, StaticBody3D> CrateBodies => _rac1CrateBodies;
    internal Dictionary<int, (Node3D Node, Rac1CrateFallSession Motion, float FootOffset)> FallingCrates => _rac1FallingCrates;
    internal List<(Node3D Node, Rac1CrateDebris Motion)> Debris => _rac1Debris;
    internal List<(Node3D Node, Rac1AmmoPickupSession Motion)> AmmoPickups => _rac1AmmoPickups;
    internal List<Rac1HostedNanotechCloud> NanotechClouds => _rac1NanotechClouds;
    internal Dictionary<int, RuntimeWorldScene.DynamicObjectNode> PlacedBoltNodes => _rac1PlacedBoltNodes;
    internal List<(Rac1PlacedBoltSession.Registration Registration, RuntimeWorldScene.DynamicObjectNode Node)> PendingPlacedBolts => _rac1PendingPlacedBolts;
    internal Rac1PickupAssets.Catalogue? PickupAssets => _rac1PickupAssets;
    internal int PickupSoundCount => _rac1PickupSoundCount;
    internal bool HasPickupModel(int nativeClass) => _rac1PickupAssets?.Models.ContainsKey(nativeClass) == true;
    internal void SetDiagnosticAssets(Rac1PickupAssets.Catalogue? assets, WorldMaterialFactory? materials)
    {
        _rac1PickupAssets = assets;
        _rac1PickupMaterials = materials;
    }
    internal void AddDebris(Node3D node, Rac1CrateDebris motion) => _rac1Debris.Add((node, motion));
    internal void DiscardPickup(int id)
    {
        _rac1PickupMotion.Remove(id);
        _presentation.PickupNodes.Remove(id);
    }
    internal void RemovePlacedBolt(int id)
    {
        _rac1PendingPlacedBolts.RemoveAll(pending => pending.Registration.PickupId == id);
        _rac1PlacedBoltNodes.Remove(id);
        DiscardPickup(id);
    }
    internal void SetCratePresence(int id, RuntimeEntityPresence presence)
    {
        if (_rac1CrateBodies.TryGetValue(id, out var body) && IsInstanceValid(body))
            body.CollisionLayer = presence == RuntimeEntityPresence.Active ? 1u : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetRac1PickupPresentation();
        ResetRac1GoldBolts();
    }
}
