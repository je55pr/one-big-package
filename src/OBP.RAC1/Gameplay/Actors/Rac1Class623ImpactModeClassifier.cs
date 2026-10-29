namespace OBP.RAC1.Gameplay;

/// <summary>
/// Provisional table branch at Rilgar 0x2849cc–0x284afc inside the
/// general 0x284730 impact service. The service reads mode +0x28,
/// submode +0x29 and, for mode zero, source kind +0x2a from its
/// live impact context. The subsequent callback/control-slot logic
/// at 0x284b30–0x284c74 may modify the FINAL status or return index.
/// This classifier is not itself hit admission or collision detection.
/// </summary>
public static class Rac1Class623ImpactModeClassifier
{
    /// <summary>
    /// Model only the verified eight-entry original mode switch and
    /// fallback branch. No value here is a verified *final* service result.
    /// An actual native context must supply all three authored/runtime
    /// fields; they cannot be inferred from Godot presentation.
    /// </summary>
    public static Rac1ProvisionalImpactClassification Classify(
        byte mode, byte submode, ushort sourceKind)
    {
        return mode switch
        {
            0 => sourceKind is 1 or 2
                ? new(2, 0, false)
                : new(2, 1, false),
            1 => submode switch
            {
                < 2 => new(2, 11, false),
                2 => new(2, 5, false),
                _ => new(2, 9, false)
            },
            2 => submode < 2
                ? new(2, 8, false)
                : new(2, 13, false),
            3 => submode switch
            {
                < 2 => new(2, 10, false),
                2 => new(2, 15, false),
                _ => new(2, 16, true)
            },
            4 => new(2, 14, false),
            5 => submode < 2
                ? new(3, 4, false)
                : new(4, 7, false),
            6 => new(2, 1, false),
            7 => submode < 2
                ? new(2, 12, false)
                : new(2, 17, false),
            _ => new(2, 1, false)
        };
    }
}

/// <param name="Status">Pre-callback proposed status, not final outStatus.</param>
/// <param name="ResponseIndex">Pre-callback proposed index, not final return v0.</param>
/// <param name="ResetsTracking">Mode-3 high-submode branch clears native
/// collision slot halfwords +0x14 and +0x1c before callback resolution.</param>
public readonly record struct Rac1ProvisionalImpactClassification(
    int Status, int ResponseIndex, bool ResetsTracking);
