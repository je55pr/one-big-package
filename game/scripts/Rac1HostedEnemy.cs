using Godot;
using OBP.Core;
using OBP.Godot;
using OBP.IO;
using OBP.RAC1.Animation;
using OBP.RAC1.Gameplay;
using OBP.RAC1.Level;
using OBP.RAC1.Player;
using OBP.RAC1.Presentation;
using OBP.Runtime;
using OBP.Runtime.Gameplay;
using NVector3 = System.Numerics.Vector3;

namespace OBP.Game;

internal sealed class Rac1HostedEnemy(RuntimeWorldScene.DynamicObjectNode node, Rac1MobyAnimationAssets.Asset asset,
    CharacterBody3D body, Rac1EnemyMotion? motion, int sequence)
{
    public RuntimeWorldScene.DynamicObjectNode Node { get; } = node;
    // Capture the imported placement before any launches, contacts or
    // deaths can move Root. Native Aridia nest relaunch heading uses
    // the same shark's authored home across multiple recycle cycles.
    public Vector3 AuthoredHomePosition { get; } = node.Root.GlobalPosition;
    public CharacterBody3D Body { get; } = body;
    public Rac1EnemyMotion? Motion { get; } = motion;
    public Rac1MobySequenceClock Animation { get; } = new(asset, sequence);
    public bool Grounded { get; set; } = true;
    public bool NeedsGroundPlacement { get; set; } = node.Source.NativeClassId is 749 or Rac1AridiaEnemies.Sandshark;
    public bool Buried { get; set; } = node.Source.NativeClassId == Rac1AridiaEnemies.Sandshark;
    public int DeathTicks { get; set; }
    public int GroundPlacementAttempts { get; set; }
    public bool RewardEmitted { get; set; }
    public bool DeathPresented { get; set; }
    public GpuParticles3D? Flame { get; set; }
    public Rac1MobyReaction? Reaction { get; set; }
}
