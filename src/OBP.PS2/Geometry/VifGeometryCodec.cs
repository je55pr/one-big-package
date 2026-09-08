namespace OBP.PS2.Geometry;

/// <summary>
/// Namespace-local bridge for shared geometry codecs. Because RcTfrag now lives
/// beneath OBP.PS2, the simple name Vif otherwise binds to the sibling
/// OBP.PS2.Vif namespace rather than its static codec type.
/// </summary>
internal static class Vif
{
    public const int UnpackV3_16 = global::OBP.PS2.Vif.Vif.UnpackV3_16;
    public const int UnpackV4_16 = global::OBP.PS2.Vif.Vif.UnpackV4_16;
    public const int UnpackV4_8 = global::OBP.PS2.Vif.Vif.UnpackV4_8;

    public static List<global::OBP.PS2.Vif.Vif.VifPacket> ReadCommandList(ArraySegment<byte> bytes, int maxPackets = 100_000) =>
        global::OBP.PS2.Vif.Vif.ReadCommandList(bytes, maxPackets);

    public static List<global::OBP.PS2.Vif.Vif.VifPacket> FilterUnpacks(IEnumerable<global::OBP.PS2.Vif.Vif.VifPacket> packets) =>
        global::OBP.PS2.Vif.Vif.FilterUnpacks(packets);
}
