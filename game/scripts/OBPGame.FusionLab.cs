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
    /// Enter OBP-authored geometry through the shared staged world lifecycle.
    /// </summary>
    private bool EnterFusionLab()
    {
        try
        {
            var definition = FusionLabDefinition.Parse(File.ReadAllText(ProjectSettings.GlobalizePath("res://data/fusion_lab.json")));
            return EnterWorld(definition.Id, () => FusionLabWorldFactory.Build(definition), null, null, definition);
        }
        catch (Exception error)
        {
            DestinationError("Fusion Lab layout is unavailable: " + error.Message);
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
                Text = station.Label + "\n" + (station.AdapterId switch
                {
                    "obp/universal-loadout/v1" => "PAUSE: weapons, gadgets, appearance and Quick Select",
                    "obp/mixed-encounter/v1" => "PAUSE > DEVELOPER: actor status and independent resets",
                    _ => "UNAVAILABLE: adapter/content not yet implemented",
                }),
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
