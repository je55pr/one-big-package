using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OBP.Core;
using OBP.Runtime.Effects;

namespace OBP.Fusion;

/// <summary>OBP-authored trigger/placement data. Native recipe parameters stay behind the registered source factory.</summary>
public sealed record EffectExperimentDefinition(int Version, string Id, string ResetKey, IReadOnlyList<EffectExperimentEntry> Effects)
{
    public static EffectExperimentDefinition Parse(string json)
    {
        var definition = JsonSerializer.Deserialize<EffectExperimentDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("Missing effect experiment.");
        definition.Validate(); return definition;
    }
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(ResetKey) || Effects is null || Effects.Count is < 1 or > 16)
            throw new ArgumentException("Unsupported effect experiment or invalid instance count.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effect in Effects)
        {
            if (effect is null || string.IsNullOrWhiteSpace(effect.Id) || !ids.Add(effect.Id) || effect.EmissionTicks is < 1 or > 36000 ||
                effect.Position is null || effect.End is null || string.IsNullOrWhiteSpace(effect.AssetKind) || string.IsNullOrWhiteSpace(effect.OriginalId))
                throw new ArgumentException("Invalid or duplicate effect experiment entry.");
            effect.Key.Validate(); effect.Pose.Validate();
            if (effect.Pose.Position.LengthSquared() > 4_000_000 || effect.Pose.End.LengthSquared() > 4_000_000)
                throw new ArgumentException("Effect experiment placement exceeds the authored bounds.");
        }
    }
    [JsonIgnore] public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
    public IReadOnlyList<EffectRequest> Requests(EffectWorldSession session, Vector3 offset = default) => Effects.Select((effect, index) =>
        new EffectRequest(session.WorldInstanceId, session.LeaseGeneration, session.ResetGeneration, index + 1,
            effect.Key, effect.Seed, new(effect.Pose.Position + offset, effect.Pose.End + offset), effect.EmissionTicks)).ToArray();
}
public sealed record EffectExperimentPoint(float X, float Y, float Z)
{
    [JsonIgnore] public Vector3 Value => new(X, Y, Z);
}
public sealed record EffectExperimentEntry(string Id,
    [property: JsonConverter(typeof(SourceGameWireJsonConverter))] ObpSourceGame Game,
    string BuildId, string LevelId, string AssetKind, string OriginalId, string Recipe,
    uint Seed, EffectExperimentPoint Position, EffectExperimentPoint End, int EmissionTicks)
{
    [JsonIgnore] public RuntimeEffectRecipeKey Key => new(new(Game, BuildId, LevelId, AssetKind, OriginalId), Recipe);
    [JsonIgnore] public RuntimeEffectPose Pose => new(Position.Value, End.Value);
}
