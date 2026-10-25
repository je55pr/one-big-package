namespace OBP.Runtime.Diagnostics;

/// <summary>
/// Wire-only v1/v2 playtest header DTO: source map keys retain their established
/// canonical JSON strings. Runtime source maps use ObpSourceGame and format keys
/// through SourceIdentityWire when constructing this compatibility envelope.
/// No paths or payloads.
/// </summary>
public sealed record PlaytestWeaponConfiguration(string LoadoutId, string DamagePolicy,
    IReadOnlyDictionary<string, string> Sources)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(LoadoutId) || LoadoutId.Length > 128 ||
            string.IsNullOrWhiteSpace(DamagePolicy) || DamagePolicy.Length > 128 || Sources is null ||
            Sources.Count != 3 || Sources.Any(s => s.Key is not ("rac1" or "rac2" or "rac3") ||
                string.IsNullOrWhiteSpace(s.Value) || s.Value.Length > 256 || s.Value.Any(char.IsControl)))
            throw new InvalidDataException("Invalid cross-game weapon configuration.");
    }

    public bool Matches(PlaytestWeaponConfiguration actual) => LoadoutId == actual.LoadoutId &&
        DamagePolicy == actual.DamagePolicy && Sources.Count == actual.Sources.Count &&
        Sources.All(s => actual.Sources.TryGetValue(s.Key, out string? value) && value == s.Value);
}
