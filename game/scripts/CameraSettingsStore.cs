using Godot;
using OBP.RAC1.Camera;

namespace OneBigPackage;

/// <summary>OBP persistence for the recovered camera options; independent of device calibration.</summary>
internal static class CameraSettingsStore
{
    internal static string ConfigPath { get; set; } = "user://controller-settings.cfg";
    private const string Section = "rac1_camera";
    private static Rac1CameraOptions? _current;

    public static Rac1CameraOptions Current => _current ??= Load();
    internal static void Reload() => _current = null;

    public static void Set(Rac1CameraOptions options, bool persist = true)
    {
        _current = options.Validate();
        if (!persist) return;
        var config = new ConfigFile();
        _ = config.Load(ConfigPath);
        config.SetValue(Section, "horizontal", (int)options.Horizontal);
        config.SetValue(Section, "vertical", (int)options.Vertical);
        config.SetValue(Section, "speed", (int)options.Speed);
        Error result = config.Save(ConfigPath);
        if (result != Error.Ok) GD.PushWarning($"Could not save camera settings: {result}");
    }

    private static Rac1CameraOptions Load()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok) return Rac1CameraOptions.Default;
        int horizontal = (int)config.GetValue(Section, "horizontal", 1);
        int vertical = (int)config.GetValue(Section, "vertical", 1);
        int speed = (int)config.GetValue(Section, "speed", 1);
        return new(
            Enum.IsDefined((Rac1CameraDirection)horizontal) ? (Rac1CameraDirection)horizontal : Rac1CameraDirection.Normal,
            Enum.IsDefined((Rac1CameraDirection)vertical) ? (Rac1CameraDirection)vertical : Rac1CameraDirection.Normal,
            Enum.IsDefined((Rac1CameraSpeed)speed) ? (Rac1CameraSpeed)speed : Rac1CameraSpeed.Medium);
    }
}
