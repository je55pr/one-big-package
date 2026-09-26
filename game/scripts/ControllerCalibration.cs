using Godot;
using System.Security.Cryptography;
using System.Text;

namespace OneBigPackage;

internal readonly record struct StickCalibration(
    float NegativeX,
    float PositiveX,
    float NegativeY,
    float PositiveY,
    float InnerDeadzone)
{
    public static StickCalibration Default => new(1f, 1f, 1f, 1f, 0f);

    public Vector2 Apply(Vector2 raw)
    {
        float x = ScaleAxis(raw.X, NegativeX, PositiveX);
        float y = ScaleAxis(raw.Y, NegativeY, PositiveY);
        float deadzone = Math.Clamp(InnerDeadzone, 0f, 0.9f);
        float magnitude = MathF.Sqrt((x * x) + (y * y));

        if (magnitude <= deadzone || magnitude <= 1e-7f)
            return Vector2.Zero;

        if (deadzone > 0f)
        {
            float remapped = Math.Clamp((magnitude - deadzone) / (1f - deadzone), 0f, 1f);
            float factor = remapped / magnitude;
            x *= factor;
            y *= factor;
        }

        return new Vector2(
            Math.Clamp(x, -1f, 1f),
            Math.Clamp(y, -1f, 1f));
    }
    public StickCalibration WithSafeLimits() => new(
        SafeExtent(NegativeX),
        SafeExtent(PositiveX),
        SafeExtent(NegativeY),
        SafeExtent(PositiveY),
        Math.Clamp(InnerDeadzone, 0f, 0.9f));

    private static float ScaleAxis(float value, float negativeExtent, float positiveExtent)
    {
        float extent = value < 0f ? SafeExtent(negativeExtent) : SafeExtent(positiveExtent);
        return Math.Clamp(value / extent, -1f, 1f);
    }

    private static float SafeExtent(float value)
    {
        if (!float.IsFinite(value))
            return 1f;
        return Math.Clamp(MathF.Abs(value), 0.05f, 1.5f);
    }
}

internal readonly record struct ControllerCalibrationProfile(
    StickCalibration Left,
    StickCalibration Right)
{
    public static ControllerCalibrationProfile Default => new(
        StickCalibration.Default,
        StickCalibration.Default);

    public ControllerCalibrationProfile WithSafeLimits() => new(
        Left.WithSafeLimits(),
        Right.WithSafeLimits());
}

internal static class ControllerCalibrationStore
{
    private const string ConfigPath = "user://controller-settings.cfg";
    private static readonly Dictionary<string, ControllerCalibrationProfile> Cache = new();

    public static ControllerCalibrationProfile GetProfile(int deviceId)
    {
        string identity = DeviceIdentity(deviceId);
        if (Cache.TryGetValue(identity, out var profile))
            return profile;
        var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok)
            return Cache[identity] = ControllerCalibrationProfile.Default;

        string section = SectionName(identity);
        profile = new ControllerCalibrationProfile(
            ReadStick(config, section, "left"),
            ReadStick(config, section, "right")).WithSafeLimits();
        Cache[identity] = profile;
        return profile;
    }

    public static Vector2 ApplyLeft(int? deviceId, Vector2 raw) =>
        deviceId is { } id ? GetProfile(id).Left.Apply(raw) : raw;

    public static Vector2 ApplyRight(int? deviceId, Vector2 raw) =>
        deviceId is { } id ? GetProfile(id).Right.Apply(raw) : raw;

    public static void SaveProfile(int deviceId, ControllerCalibrationProfile profile)
    {
        string identity = DeviceIdentity(deviceId);
        profile = profile.WithSafeLimits();
        Cache[identity] = profile;

        var config = new ConfigFile();
        _ = config.Load(ConfigPath);
        string section = SectionName(identity);
        config.SetValue(section, "identity", identity);
        config.SetValue(section, "name", Input.GetJoyName(deviceId));
        WriteStick(config, section, "left", profile.Left);
        WriteStick(config, section, "right", profile.Right);
        Error error = config.Save(ConfigPath);
        if (error != Error.Ok)
            GD.PushWarning($"Could not save controller settings: {error}");
    }

    public static void ResetProfile(int deviceId)
    {
        string identity = DeviceIdentity(deviceId);
        Cache.Remove(identity);

        var config = new ConfigFile();
        if (config.Load(ConfigPath) != Error.Ok)
            return;

        string section = SectionName(identity);
        if (config.HasSection(section))
            config.EraseSection(section);

        Error error = config.Save(ConfigPath);
        if (error != Error.Ok)
            GD.PushWarning($"Could not reset controller settings: {error}");
    }
    private static StickCalibration ReadStick(ConfigFile config, string section, string prefix) => new(
        ReadFloat(config, section, $"{prefix}_neg_x", 1f),
        ReadFloat(config, section, $"{prefix}_pos_x", 1f),
        ReadFloat(config, section, $"{prefix}_neg_y", 1f),
        ReadFloat(config, section, $"{prefix}_pos_y", 1f),
        ReadFloat(config, section, $"{prefix}_deadzone", 0f));

    private static void WriteStick(
        ConfigFile config,
        string section,
        string prefix,
        StickCalibration stick)
    {
        config.SetValue(section, $"{prefix}_neg_x", stick.NegativeX);
        config.SetValue(section, $"{prefix}_pos_x", stick.PositiveX);
        config.SetValue(section, $"{prefix}_neg_y", stick.NegativeY);
        config.SetValue(section, $"{prefix}_pos_y", stick.PositiveY);
        config.SetValue(section, $"{prefix}_deadzone", stick.InnerDeadzone);
    }

    private static float ReadFloat(
        ConfigFile config,
        string section,
        string key,
        float fallback) =>
        (float)config.GetValue(section, key, fallback);

    private static string DeviceIdentity(int deviceId)
    {
        string guid = Input.GetJoyGuid(deviceId);
        return !string.IsNullOrWhiteSpace(guid)
            ? $"guid:{guid}"
            : $"name:{Input.GetJoyName(deviceId)}";
    }

    private static string SectionName(string identity)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"controller_{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }
}
