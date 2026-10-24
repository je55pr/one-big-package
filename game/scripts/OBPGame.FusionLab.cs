using System.IO;
using Godot;
using OBP.Core;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;

namespace OneBigPackage;

public partial class OBPGame
{
    private Label? _fusionLabStatus;

    /// <summary>
    /// Enter original OBP-authored geometry via the existing RuntimeWorld,
    /// WorldHost and PlayerHost route, rather than a parallel character engine.
    /// </summary>
    private bool EnterFusionLab()
    {
        FusionLabDefinition definition;
        RuntimeWorld world;
        RuntimeWorldScene.Result preparedScene;
        try
        {
            string path = ProjectSettings.GlobalizePath("res://data/fusion_lab.json");
            definition = FusionLabDefinition.Parse(File.ReadAllText(path));
            world = FusionLabWorldFactory.Build(definition);
            preparedScene = WorldHost.PrepareScene(world, "FusionLab", WorldOptions(world));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[fusion-lab] layout preparation failed: {ex}");
            _destinationSelector?.SetHint($"Fusion Lab unavailable: {ex.Message}", error: true);
            return false;
        }

        try
        {
            _destinationSelector?.QueueFree();
            _destinationSelector = null;
            _sourceManager?.QueueFree();
            _sourceManager = null;
            TeardownWorld();
            ClearBootstrapSceneArtifacts();

            _world = world;
            _sceneResult = _worldHost.Load(this, _worldRoot, world, "FusionLab",
                WorldOptions(world), preparedScene);

            // The world is custom, but a verified native R&C1 avatar can be
            // requested through its actual provider when the source is present.
            // Without that source, the host's clearly non-native debug capsule
            // remains controllable. No fake Ratchet asset is substituted.
            EnsureSourceLibraryInitialized();
            bool nativeAvatarAvailable = _sources.Get(ObpSourceGame.Rac1) is not null;
            _activeDestination = nativeAvatarAvailable
                ? TrilogyWorldProviders.Resolve("rac1:LEVEL0")
                : null;

            SetupOverlay(_sceneResult, world);
            SpawnPlayer(world);
            if (_player is { } player)
                StartFusionLabTraversalSmoke(player);
            _mode = Mode.World;
            _sceneKind = "fusion-lab";
            _currentDestinationId = definition.Id;
            _genericNavigationActive = true;
            _worldSwitches++;

            DecorateFusionLab(definition, nativeAvatarAvailable);
            EnsurePlayerHud();
            EnsureWorldHud();
            UpdatePlayerHud();
            UpdateWorldHud();
            _loadSummary = $"Fusion Lab / {definition.Rooms.Count} rooms / {definition.Stations.Count} future stations";
            GD.Print($"[fusion-lab] {_loadSummary}: one PlayerHost, original collision, no source-world importer");
            _playtest?.WorldEntered();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[fusion-lab] host activation failed: {ex}");
            if (GodotObject.IsInstanceValid(preparedScene.Root))
                preparedScene.Root.QueueFree();
            ShowDestinationSelector();
            _destinationSelector?.SetHint($"Fusion Lab could not start: {ex.Message}", error: true);
            return false;
        }
    }

    private void DecorateFusionLab(FusionLabDefinition definition, bool nativeAvatarAvailable)
    {
        if (_sceneResult is null) return;
        var root = _sceneResult.Root;

        // Lights and lettering are original lab presentation, never native
        // source assets. They are parented to the world for automatic teardown.
        foreach (var room in definition.Rooms)
        {
            var light = new OmniLight3D
            {
                Name = "Light_" + room.Id,
                Position = RuntimeWorldScene.ToScene(room.CenterX, 4.2, room.CenterZ),
                LightColor = new Color((float)room.Tint[0], (float)room.Tint[1], (float)room.Tint[2]),
                LightEnergy = 1.5f,
                OmniRange = 22f,
            };
            root.AddChild(light);

            var sign = new Label3D
            {
                Name = "RoomSign_" + room.Id,
                Text = room.Label.ToUpperInvariant(),
                Position = RuntimeWorldScene.ToScene(room.CenterX, 3.15, room.CenterZ),
                FontSize = 52,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = Colors.White,
            };
            root.AddChild(sign);
        }

        foreach (var station in definition.Stations)
        {
            var sign = new Label3D
            {
                Name = "Station_" + station.Id,
                Text = station.Label + "\nUNAVAILABLE: adapter/content not yet implemented",
                Position = RuntimeWorldScene.ToScene(
                    station.Position.X, 2.0 + station.Position.Y, station.Position.Z),
                FontSize = 31,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.74f, 0.38f),
            };
            root.AddChild(sign);
        }

        _fusionLabStatus = new Label
        {
            Name = "FusionLabStatus",
            Text = "OBP FUSION LAB  |  GREYBOX PLAYTEST  |  " +
                (nativeAvatarAvailable ? "R&C1 avatar source connected" :
                "AVATAR SOURCE UNAVAILABLE: DEBUG CAPSULE") +
                "\nRooms: Entrance > Hub > Test Chamber > Return   |   ESC: pause/back",
            Position = new Vector2(28, 16),
            Modulate = new Color(0.88f, 0.93f, 1f),
        };
        _ui.AddChild(_fusionLabStatus);
    }

    private void ClearFusionLabStatus()
    {
        if (_fusionLabStatus is not null && IsInstanceValid(_fusionLabStatus))
            _fusionLabStatus.QueueFree();
        _fusionLabStatus = null;
    }
}
