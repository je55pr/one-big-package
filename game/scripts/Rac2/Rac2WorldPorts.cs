using Godot;
using OBP.Godot;
using OBP.RAC2.Gameplay;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>Only scene and player-service edges required by the R&C2 source world.
/// No root reference, new native authority, or default debug encounter.</summary>
internal sealed record Rac2WorldPorts(Func<bool> Current, Func<bool> SourceValid, Func<bool> Ordinary, Func<bool> Alive,
    Func<bool> HasNativePlayer, Func<ActorWorldHost> Actors, Action<RuntimeWorldScene.DynamicObjectNode> Audio,
    Action<Vector3, Rac2Class500Payout> CrateReward,
    Func<RuntimeWorldScene.DynamicObjectNode, int, int, ulong, bool> PlayerDamage,
    bool CrateFocus, bool CrateAutoStrike);
