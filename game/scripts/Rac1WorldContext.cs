using OBP.RAC1.Gameplay;
using OBP.RAC1.Player;

namespace OBP.Game;

/// <summary>
/// Native controllers for one scene lease. Campaign, inventory, health and wallet
/// remain with their existing persistent owners; no native rules are implemented here.
/// </summary>
internal sealed class Rac1WorldContext
{
    internal Rac1WorldContext(int levelId, Rac1PickupRandom random)
    {
        MobyPersistence = new(levelId);
        PlacedBolts = new(MobyRuntime);
        BoltCrates = new(MobyRuntime, MobyPersistence, BoltPickups);
        Hostiles = new(MobyRuntime, random);
    }

    internal Rac1MobyPersistenceSession MobyPersistence { get; }
    internal Rac1MobyRuntimeSession MobyRuntime { get; } = new();
    internal Rac1PlacedBoltSession PlacedBolts { get; }
    internal Rac1BoltPickupSession BoltPickups { get; } = new();
    internal Rac1BoltCrateSession BoltCrates { get; }
    internal Rac1Class749HostileSession Hostiles { get; }
    internal Rac1DamageTransportSession DamageTransport { get; } = new();
    internal Rac1BombGloveSession? BombGlove { get; set; }
    internal bool EquipmentUsePressed { get; set; }
    internal bool BombFireRequested { get; set; }
    internal Rac1BombGloveContactResolution? LastBombContactResolution { get; set; }
    // Raw retail player-state word at +0x20a4. Its semantics remain unnamed.
    internal int NativePlayerState20A4 { get; set; }
    internal Rac1RatchetNanotechSnapshot? LastEnvironmentalDeathBoundary { get; set; }
    internal double LastEnvironmentalDeathContactSeparation { get; set; } = double.NaN;
    internal string CombatStatus { get; set; } = "off";
}
