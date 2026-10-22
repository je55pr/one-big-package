using System.Security.Cryptography;

namespace OBP.Tests;

public sealed class Rac1EffectNativeSourceTests
{
    [SkippableFact]
    public void ParticleRecurrencesTextureLoaderAndTeslaDrawAreBoundToTheInspectedNativeWindows()
    {
        var read = Rac1AirborneNativeSourceTests.ReadOverlay();
        (int Address, int Size, string Sha256)[] windows =
        [
            (0x2690b8, 0x410, "e71776b32f58e6ac45835bb1cb7f4948294e911eb0b5687302e3a876e9ecd0b7"), // Type2.
            (0x269958, 0x238, "d35841567d8ac21f11035e2f7aff8f862d20a420fbabf86f42964a45b0aa0f06"), // Type4.
            (0x26a4c8, 0x228, "2028f2f4399ba4a9975d6cd3d1332fd565b2fd1c1ccc048edec846f6989c1aa5"), // Type8.
            (0x26a9f0, 0xd80, "db40ad41384bfc842f1d184c994b02185a573d4e129dd4c59fde36650ab99d76"), // Types11/12.
            (0x26bb70, 0x7d0, "e2c54dfa0a1d40101f524be95767f170f2599cde38893c522dbf9983b8b1578a"), // Types15/16.
            (0x25e450, 0xf98, "835a097f2ca1e0f79338b9ac9f25027c8ec677bbf021bfbb87cad347c7e7b3c1"), // Weapon recipes.
            (0x2ba970, 0x740, "a61d22266d3ca7755889cf29bd426af3a1868803a9074ce0e6bbf4e14132f105"), // Tesla ribbons.
            (0x161594, 0xac, "72c7f554c2fec5a7f524ec29559e8a832173ee06714f208282d210c8baebbf80"), // Tesla parameters.
            (0x23f768, 0xd0, "78dd661dbc665f4e0a3c589eaa30d8752f7b117606ec469fd0db4fca7773089a"), // Fx bank loader.
            (0x261480, 0x190, "c3cff8b001fb6fd83b836cf30cb68b1244c0796ff6dbdeeaacaa6fb0c18855e0"), // Direction packing.
        ];
        foreach (var window in windows)
            Assert.Equal(window.Sha256, Convert.ToHexString(SHA256.HashData(read(window.Address, window.Size))).ToLowerInvariant());
    }
}
