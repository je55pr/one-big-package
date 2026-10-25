using OBP.Core;
using System.Text.Json.Serialization;

namespace OBP.Fusion;

/// <summary>Source-qualified native identity; never a cross-game behavioural claim.</summary>
public sealed record FusionSourceIdentity([property: JsonConverter(typeof(SourceGameWireJsonConverter)), JsonRequired] ObpSourceGame Game, string Build, string NativeId, string? Variant = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Game) || string.IsNullOrWhiteSpace(Build) || string.IsNullOrWhiteSpace(NativeId))
            throw new ArgumentException("Fusion identities require game, build and native id.");
    }
}
