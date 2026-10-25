using OBP.Core;
using System.Text.Json.Serialization;
using OBP.Core.Math;

namespace OBP.Fusion;

/// <summary>Source-qualified native identity; never a cross-game behavioural claim.</summary>
public sealed record FusionSourceIdentity([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame Game, string Build, string NativeId, string? Variant = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Game) || string.IsNullOrWhiteSpace(Build) ||
            string.IsNullOrWhiteSpace(NativeId))
            throw new ArgumentException("Fusion identities require game, build and native id.");
    }
}

/// <summary>
/// An optional live exhibit described without source decoding or Godot coupling.
/// Adapters are resolved by the host, never inferred from a native asset identifier.
/// </summary>
public sealed record FusionExhibit
{
    public required string Id { get; init; }
    public required FusionSourceIdentity Source { get; init; }
    public required string AdapterId { get; init; }
    public required Vec3 Position { get; init; }
    public CompositionTransform Transform { get; init; } = CompositionTransform.Identity;
    public string? ResetKey { get; init; }
}

/// <summary>Versioned engine-neutral scenario; the host owns actual actor instances.</summary>
public sealed record FusionSession
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public required string Id { get; init; }
    public IReadOnlyList<FusionExhibit> Exhibits { get; init; } = Array.Empty<FusionExhibit>();

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new ArgumentException($"Unsupported Fusion session version {Version}.");
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Fusion session id is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exhibit in Exhibits)
        {
            if (string.IsNullOrWhiteSpace(exhibit.Id) || !ids.Add(exhibit.Id))
                throw new ArgumentException($"Invalid or duplicate exhibit id '{exhibit.Id}'.");
            if (string.IsNullOrWhiteSpace(exhibit.AdapterId))
                throw new ArgumentException($"Exhibit '{exhibit.Id}' lacks a supported adapter id.");
            exhibit.Source.Validate();
            if (!double.IsFinite(exhibit.Position.X) ||
                !double.IsFinite(exhibit.Position.Y) ||
                !double.IsFinite(exhibit.Position.Z))
                throw new ArgumentException($"Exhibit '{exhibit.Id}' has nonfinite position.");
            if (!double.IsFinite(exhibit.Transform.Scale) || exhibit.Transform.Scale <= 0)
                throw new ArgumentException($"Exhibit '{exhibit.Id}' has invalid scale.");
        }
    }
}
