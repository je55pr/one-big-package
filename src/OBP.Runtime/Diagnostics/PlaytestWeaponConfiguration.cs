namespace OBP.Runtime.Diagnostics;

/// <summary>Explicit opt-in loadout and locally verified source identities; no paths or payloads.</summary>
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
