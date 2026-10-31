using OBP.Core;
using OBP.Fusion;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.RAC1.Progression;
using OBP.Runtime.Gameplay;
using OBP.Runtime.Player;

namespace OBP.Game;

/// <summary>
/// Sole persistent ordinary-player owner: native state and progression outlive
/// worlds, destinations, and menu pages. WorldEntrySession is a lease, not a new player.
/// </summary>
internal interface IRac1WeaponPlayerInput
{
    void PrimaryAttack();
    void StepInput();
    void Select(Rac1WeaponId weapon);
    void Cycle(int direction);
}

internal sealed class PlayerSessionHost
{
    // Original shared native effect RNG continues across world leases and replay resets.
    private Rac1PickupRandom? _rac1EffectRandom;
    internal Rac1PickupRandom Rac1EffectRandom(int? seed) => _rac1EffectRandom ??=
        Rac1PickupRandom.FromSeed(unchecked((uint)(seed ?? Environment.TickCount)));
    internal void ResetRac1EffectRandom(int seed) =>
        _rac1EffectRandom = Rac1PickupRandom.FromSeed(unchecked((uint)seed));

    private PlayerEquipmentFacade? _equipment;
    internal PlayerEquipmentFacade Equipment => _equipment ?? throw new InvalidOperationException("Player equipment ports are not bound.");

    internal void BindEquipment(PlayerEquipmentPorts ports)
    {
        if (_equipment is not null) throw new InvalidOperationException("Player equipment ports are already bound.");
        _equipment = new(ports);
    }

    internal void RetireEquipment() => _equipment?.Dispose();

    internal Rac1RatchetNanotechSession ParityHealth { get; private set; } = new();
    internal Rac1HeldItemSession ParityHeld { get; } = new();
    internal Rac1AdditionalUseHost Rac1Use { get; } = new();
    internal IRac1WeaponPlayerInput? CombatInput { get; set; }

    internal int SelectRac1CrateReward(int centre, int? seed)
    {
        var range = Rac1BoltCrate.RewardRange(centre);
        return range.Maximum == 0 ? 0 : Rac1EffectRandom(seed).Inclusive(range.Minimum, range.Maximum);
    }
    internal Rac1WeaponInventory? WeaponPreview { get; set; }

    /// <summary>
    /// Enter or leave the development arsenal using the existing player-native
    /// preview controller and its current inventory. Returns whether the
    /// world presentation/use lease needs its original reset. Refill of the
    /// same preview inventory must not erase live cargo.
    /// </summary>
    internal bool UpdateRac1WeaponPreview(bool disable, Func<Rac1WeaponInventory> currentInventory,
        Rac1WeaponInventory campaignInventory)
    {
        // Native preview can swap the inventory identity. Resolve current
        // inventory only afterwards, matching the original root ordering.
        Native?.SetEquipmentPreview(!disable);
        if (disable)
        {
            WeaponPreview = null;
            return true;
        }

        WeaponPreview ??= Native?.Inventory ?? new(campaignInventory.CapturePersistentState(), currentInventory().CurrentItemId);
        // _rac1Weapons resolves to the new preview inventory after the above
        // assignment in parity mode. Never grant development items to campaign.
        var inventory = currentInventory();
        inventory.AcquireNativeItem(Rac1MetalDetectorSession.NativeGadgetItem);
        foreach (var weapon in Rac1WeaponProfiles.Additional.Select(profile => profile.Item).Append(Rac1WeaponId.FirstRanged))
        {
            inventory.AcquireNativeItem((int)weapon);
            inventory.GrantAmmoClamped((int)weapon, Rac1ItemAmmoDescriptors.Get((int)weapon).MaxAmmo);
        }
        return Rac1Use.BoundInventory != currentInventory();
    }

    internal Rac1WrenchCombatController Wrench { get; } = new();
    internal Rac1BoltWallet Rac1Wallet(Rac1BoltWallet parityWallet) => Native?.Wallet ?? parityWallet;
    internal Rac1RatchetNanotechSession Rac1Health => Native?.Health ?? ParityHealth;
    internal Rac1HeldItemSession Rac1Held => Native?.Held ?? ParityHeld;
    internal Rac1WeaponInventory Rac1Weapons(FusionWeaponSession? fusion, bool fusionTest, Rac1WeaponInventory campaign) =>
        Native?.Inventory ?? (fusionTest ? fusion!.Rac1Inventory : WeaponPreview ?? campaign);
    internal void BeginParityWorld() { if (Native is null) ParityHealth = new(); }

    internal ObpPlayerSession? Session { get; private set; }
    internal PlayerSessionNativeState? Native { get; private set; }
    internal WorldEntrySession? WorldEntries { get; private set; }
    internal WorldEntryMode? EntryMode { get; private set; }
    internal PlayerControlProfile? ControlProfile { get; private set; }
    internal PlayerAnimationProfile? AnimationProfile { get; private set; }
    internal (long Length, long Written)? AvatarSourceStamp { get; set; }

    internal bool Ensure(WorldEntryMode entryMode, Rac1WeaponInventory campaignWeapons, int campaignBolts, int seed, IPlayerSessionPreferencesAdapter preferences)
    {
        if (Session is not null) return false;
        EntryMode = entryMode;
        bool diagnostic = entryMode == WorldEntryMode.Diagnostic;
        var provider = diagnostic ? (IPlayerAvatarProvider)DiagnosticAvatarProvider.Instance : Rac1PlayerAvatarProvider.Instance;
        var avatar = new PlayerAvatarIdentity(provider.SourceGame, provider.BuildId, provider.DefaultAvatarId,
            diagnostic ? "obp:diagnostic-capsule" : "rac1:ratchet:moby-class-0");
        var profile = new SourceProfileKey(provider.SourceGame, provider.BuildId,
            diagnostic ? "obp/diagnostic-player/v1" : "rac1/ordinary-player/v1");
        var configuration = new PlayerSessionConfiguration(avatar, profile, profile, PlayerHostProfiles.Services, 60);
        if (entryMode == WorldEntryMode.OrdinaryFusion)
            Native = new(campaignWeapons, campaignBolts, seed);
        var initial = new PlayerSessionSnapshot("obp-player", 0, configuration,
            new(new SourceWeaponKey(ObpSourceGame.Rac1, 10), null, []), new(4, 4, true), new Dictionary<string, long>());
        Session = new(initial, Native, preferences);
        WorldEntries = new(Session);
        return true;
    }

    internal void ResolvePresentationProfiles()
    {
        var selected = Session?.Capture().Configuration ?? throw new InvalidOperationException("Player session not initialized.");
        (ControlProfile, AnimationProfile) = PlayerHostProfiles.Registry.Resolve(selected);
    }
}
