using System.Text.Json;
using System.Text.Json.Serialization;

namespace OBP.Runtime.Diagnostics;

/// <summary>OBP diagnostic data. Checkpoints are observations, never executable restore commands.</summary>
public sealed record PlaytestRunHeader(
    int Version, string Destination, string SourceBuild, Guid AssemblyId, string EngineVersion,
    string Platform, string PhysicsEngine, int PhysicsHz, int Seed, int Width, int Height,
    DateTimeOffset StartedUtc, JsonElement Campaign, JsonElement CameraOptions, PlaytestCheckpoint InitialState)
{
    public const int CurrentVersion = 1;
    public IReadOnlyDictionary<string, Guid> Assemblies { get; init; } = new Dictionary<string, Guid>();
    public string ApplicationVersion { get; init; } = "";
    public PlaytestWeaponConfiguration? Weapons { get; init; }
    public void Validate()
    {
        if (Version != CurrentVersion || AssemblyId == Guid.Empty || PhysicsHz != 60 ||
            Width is < 64 or > 16384 || Height is < 64 or > 16384 ||
            string.IsNullOrWhiteSpace(Destination) || Destination.Length > 128 || Destination.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(SourceBuild) || SourceBuild.Length > 128 ||
            string.IsNullOrWhiteSpace(EngineVersion) || EngineVersion.Length > 128 ||
            Platform is null || Platform.Length > 128 || PhysicsEngine is null || PhysicsEngine.Length > 128 ||
            Campaign.ValueKind != JsonValueKind.Object || CameraOptions.ValueKind != JsonValueKind.Object ||
            Campaign.GetRawText().Length > 65536 || CameraOptions.GetRawText().Length > 4096 ||
            ApplicationVersion is null || ApplicationVersion.Length > 256 || ApplicationVersion.Any(char.IsControl))
            throw new InvalidDataException("Unsupported or incomplete playtest header.");
        if (InitialState is null) throw new InvalidDataException("Recording lacks its initial observation.");
        if (Assemblies is null || Assemblies.Count is < 1 or > 64 ||
            Assemblies.Any(a => string.IsNullOrWhiteSpace(a.Key) || a.Key.Length > 128 ||
                a.Key.IndexOfAny(['/', '\\', ':']) >= 0 || a.Value == Guid.Empty))
            throw new InvalidDataException("Recording lacks valid assembly identities.");
        InitialState.Validate();
        Weapons?.Validate();
    }

    public bool MatchesAssemblies(IReadOnlyDictionary<string, Guid> actual) =>
        Assemblies.Count == actual.Count && Assemblies.All(a => actual.TryGetValue(a.Key, out var id) && id == a.Value);
}

public enum PlaytestStepKind { Physics, Process }
public enum PlaytestInputKind { Key, JoyButton, JoyAxis, MouseButton, MouseMotion, Action }

/// <summary>Only game-window input; no text, device names, credentials or source paths.</summary>
public sealed record PlaytestInputEvent(
    PlaytestInputKind Kind, ulong Milliseconds, int Code = 0, int PhysicalCode = 0,
    bool Pressed = false, bool Echo = false, int Modifiers = 0, float Value = 0,
    float X = 0, float Y = 0, float RelativeX = 0, float RelativeY = 0, string? Action = null, bool DoubleClick = false)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || Code < 0 || PhysicalCode < 0 || Modifiers is < 0 or > 15 ||
            !float.IsFinite(Value) || Value is < -1 or > 1 ||
            !float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(RelativeX) || !float.IsFinite(RelativeY) ||
            Math.Abs(X) > 1e7 || Math.Abs(Y) > 1e7 || Math.Abs(RelativeX) > 1e7 || Math.Abs(RelativeY) > 1e7 ||
            Kind == PlaytestInputKind.JoyButton && Code > 127 || Kind == PlaytestInputKind.JoyAxis && Code > 15 ||
            Kind == PlaytestInputKind.MouseButton && Code > 32 ||
            Kind == PlaytestInputKind.Action && (string.IsNullOrWhiteSpace(Action) || Action.Length > 128))
            throw new InvalidDataException("Invalid recorded input event.");
    }
}

/// <summary>Post-calibration logical controls actually consumed by a player update.</summary>
public sealed record PlaytestPlayerInput(float MoveX, float MoveY, float CameraX, float CameraY,
    bool Jump, bool Crouch, bool ActionPressed, bool UseHeld, bool UsePressed, bool ActionHeld)
{
    public void Validate()
    {
        if (new[] { MoveX, MoveY, CameraX, CameraY }.Any(v => !float.IsFinite(v) || Math.Abs(v) > 2))
            throw new InvalidDataException("Invalid recorded player controls.");
    }
}

public sealed record PlaytestValue(double[]? Numbers = null, string? Text = null, double Tolerance = 0)
{
    public void Validate()
    {
        if ((Numbers is null) == (Text is null) || !double.IsFinite(Tolerance) || Tolerance is < 0 or > 1 ||
            Numbers is { } numbers && (numbers.Length is < 1 or > 128 || numbers.Any(v => !double.IsFinite(v))) ||
            Text is { Length: > 4096 }) throw new InvalidDataException("Invalid recorded observation.");
    }
}

public sealed record PlaytestCheckpoint(IReadOnlyDictionary<string, PlaytestValue> Values)
{
    public void Validate()
    {
        if (Values is null || Values.Count > 100_000) throw new InvalidDataException("Invalid playtest checkpoint size.");
        foreach (var (key, value) in Values)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || value is null)
                throw new InvalidDataException("Invalid playtest checkpoint key.");
            value.Validate();
        }
    }

    public static PlaytestDifference? Compare(PlaytestCheckpoint expected, PlaytestCheckpoint actual, int step)
    {
        expected.Validate(); actual.Validate();
        foreach (string key in expected.Values.Keys.Union(actual.Values.Keys).Order(StringComparer.Ordinal))
        {
            if (!expected.Values.TryGetValue(key, out var a)) return new(step, key, null, actual.Values[key], "Unexpected field");
            if (!actual.Values.TryGetValue(key, out var b)) return new(step, key, a, null, "Missing field");
            if (a.Text != b.Text || (a.Numbers is null) != (b.Numbers is null) ||
                a.Numbers is { } av && b.Numbers is { } bv &&
                (av.Length != bv.Length || av.Where((v, i) => Math.Abs(v - bv[i]) > a.Tolerance).Any()))
                return new(step, key, a, b, "State differs");
        }
        return null;
    }
}

public sealed record PlaytestDifference(int Step, string Field, PlaytestValue? Expected, PlaytestValue? Actual, string Reason);
public sealed record PlaytestStep(int Index, PlaytestStepKind Kind, ulong Milliseconds, double Delta,
    IReadOnlyList<PlaytestInputEvent> Events, IReadOnlyDictionary<string, float> Actions,
    IReadOnlyList<PlaytestPlayerInput> Players, PlaytestCheckpoint? Checkpoint = null)
{
    public int Width { get; init; } = 960;
    public int Height { get; init; } = 720;
    public void Validate()
    {
        if (Index is < 0 or >= 1_000_000 || !Enum.IsDefined(Kind) || !double.IsFinite(Delta) || Delta is <= 0 or > .1 ||
            Events is null || Events.Count > 8192 || Actions is null || Actions.Count > 512 ||
            Players is null || Players.Count > 4 || Kind == PlaytestStepKind.Process && Players.Count != 0 ||
            Width is < 64 or > 16384 || Height is < 64 or > 16384)
            throw new InvalidDataException("Invalid playtest step.");
        foreach (var e in Events) { if (e is null) throw new InvalidDataException("Null recorded event."); e.Validate(); }
        foreach (var p in Players) { if (p is null) throw new InvalidDataException("Null recorded player controls."); p.Validate(); }
        foreach (var (key, strength) in Actions)
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || !float.IsFinite(strength) || strength is < 0 or > 1)
                throw new InvalidDataException("Invalid recorded action strength.");
        Checkpoint?.Validate();
    }
}

public sealed record PlaytestMarker(int Step, string Label);
public sealed record PlaytestRunEnd(int Steps, string Reason);
public sealed record PlaytestRunEntry(PlaytestRunHeader? Header = null, PlaytestStep? Step = null,
    PlaytestMarker? Marker = null, PlaytestRunEnd? End = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32,
    };
}
