using OBP.Core;
using OBP.RAC1;
using OBP.RAC2;
using OBP.RAC3;

namespace OneBigPackage;

/// <summary>
/// Composition-root catalogue of the three primary OBP retail authorities.
/// Game-specific projects own the authority facts; the application merely
/// presents them to the shared <see cref="OBP.PS2.ObpSourceLibrary"/>.
/// </summary>
public static class TrilogySourceDefinitions
{
    public static readonly IReadOnlyList<ObpSourceDefinition> All = Array.AsReadOnly(new[]
    {
        new ObpSourceDefinition("Ratchet & Clank", Rac1Authority.Primary, Rac1Authority.PrimaryIsoSizeBytes),
        new ObpSourceDefinition("Going Commando", Rac2Authority.Primary, Rac2Authority.PrimaryIsoSizeBytes),
        new ObpSourceDefinition("Up Your Arsenal", Rac3Authority.Primary, Rac3Authority.PrimaryIsoSizeBytes),
    });
}
