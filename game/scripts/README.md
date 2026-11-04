# Game script layout

OBPGame.cs stays at the top level because the Godot scene and root-boundary audit identify it there.

Code is grouped by responsibility: Application, Actors, CaptureReplay,
Fusion, Player, Sources, UI, World, and source-qualified Rac1, Rac2, Rac3.
R&C1 has smaller subfolders for enemies, weapons, campaign, player, UI, world, and
audio/effects. This move does not change namespaces, behavior or source authority.

When moving a Godot C# script, preserve its paired .cs.uid file and update scene,
test-project, and architecture paths. Use the repository UID check tool.