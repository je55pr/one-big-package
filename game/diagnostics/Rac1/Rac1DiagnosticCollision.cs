using OBP.RAC1;
using OBP.Runtime;

namespace OBP.Game.Diagnostics;

/// <summary>Authored test geometry using the original R&C1 material vocabulary, explicitly qualified like every real collision source.</summary>
internal static class Rac1DiagnosticCollision
{
    internal static RuntimeCollisionBlob Create(double[] positions, int[] indices, int[] materials) =>
        new(1, positions, indices, materials) { MaterialSource = Rac1Authority.Primary };
}
