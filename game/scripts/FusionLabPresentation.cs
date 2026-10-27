using Godot;
using OBP.Fusion;
using OBP.Godot;
using OBP.Runtime;

namespace OBP.Game;

/// <summary>World-owned authored Lab decor and one diagnostic overlay, never a world loader.</summary>
internal sealed class FusionLabPresentation
{
    private Label? _status;

    internal void Decorate(FusionLabDefinition definition, RuntimeWorldScene.Result? scene,
        CanvasLayer ui, bool diagnostic, bool nativeAvatarAvailable)
    {
        if (scene is null) return;
        Clear();
        var root = scene.Root;
        // Geometry remains attached to the one active world lease for automatic retirement.
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
            string hint = station.AdapterId switch
            {
                "obp/universal-loadout/v1" => "PAUSE: weapons, gadgets, appearance and Quick Select",
                "obp/mixed-encounter/v1" => diagnostic ? "PAUSE > DEVELOPER: actor status and independent resets" : string.Empty,
                "obp/effects-experiment/v1" => diagnostic ? "PAUSE > DEVELOPER: start/reset native effects\nR&C1 Novalis / GC Museum / UYA Cannon" : string.Empty,
                _ => "UNAVAILABLE: feature not yet implemented",
            };
            var sign = new Label3D
            {
                Name = "Station_" + station.Id,
                Text = string.IsNullOrEmpty(hint) ? station.Label : station.Label + "\n" + hint,
                Position = RuntimeWorldScene.ToScene(station.Position.X, 2.0 + station.Position.Y, station.Position.Z),
                FontSize = 31,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.74f, 0.38f),
            };
            root.AddChild(sign);
        }

        if (!diagnostic) return;
        _status = new Label
        {
            Name = "FusionLabStatus",
            Text = "OBP FUSION LAB  |  PLAYTEST" +
                (nativeAvatarAvailable ? "  |  Avatar available" : "  |  Diagnostic capsule") +
                "\nRooms: Entrance > Hub > Test Chamber > Return   |   ESC: pause/back",
            Position = new Vector2(28, 16),
            Modulate = new Color(0.88f, 0.93f, 1f),
        };
        ui.AddChild(_status);
    }

    internal void UpdateStatus(int placed, int total, IReadOnlyDictionary<string, string> problems)
    {
        if (_status is null || !GodotObject.IsInstanceValid(_status)) return;
        string suffix = placed < total
            ? " — preparing original assets in the background" : " — live native controllers";
        if (problems.Count > 0)
            suffix = "\n" + string.Join("\n", problems.Select(pair => pair.Key + ": " + pair.Value));
        _status.Text = "OBP FUSION LAB  |  PAUSE: LOADOUT / SETTINGS\n" +
            $"Mixed encounter: {placed}/{total} source actors" + suffix;
    }

    internal void Clear()
    {
        if (_status is not null && GodotObject.IsInstanceValid(_status))
        {
            _status.ProcessMode = Node.ProcessModeEnum.Disabled;
            _status.QueueFree();
        }
        _status = null;
    }
}
