using OBP.Core;
using System.Buffers.Binary;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>Retail authored class-816 trigger cuboid, expressed in OBP's Y-up space.</summary>
public sealed record Rac1TaxiTriggerVolume(
    Rac1TaxiPoint Center, Rac1TaxiPoint HalfExtents)
{
    public bool Contains(Rac1TaxiPoint point) =>
        Math.Abs(point.X - Center.X) <= HalfExtents.X &&
        Math.Abs(point.Y - Center.Y) <= HalfExtents.Y &&
        Math.Abs(point.Z - Center.Z) <= HalfExtents.Z;
}

public sealed record Rac1MetropolisTaxiStop(
    int InstanceIndex,
    int NativeClassId,
    Rac1TaxiPoint Position,
    IReadOnlyList<Rac1TaxiPoint> OutboundPath,
    string DestinationLabel,
    IReadOnlyList<Rac1TaxiPoint>? ReturnPath = null,
    IReadOnlyList<Rac1TaxiPoint>? ApproachPath = null,
    IReadOnlyList<Rac1TaxiPoint>? DeparturePath = null,
    Rac1TaxiTriggerVolume? ApproachTrigger = null,
    int NativeInitialSide = -1,
    bool NativeSeekEvidenced = false,
    Rac1TaxiHoverPhases? NativeHoverPhases = null,
    Rac1TaxiNativeHud? NativeHudSelectors = null,
    int? NativeFirstDepartureSelector = null,
    IReadOnlyList<int>? NativeDepartureSelectors = null,
    Rac1TaxiNativeShuttleDock? NativeShuttleDock = null,
    Rac1TaxiNativeHudElementLease? NativeHudElementLease = null);

public readonly record struct Rac1TaxiPoint(double X, double Y, double Z)
{
    public double DistanceSquared(Rac1TaxiPoint other)
    {
        double x = X - other.X, y = Y - other.Y, z = Z - other.Z;
        return x * x + y * y + z * z;
    }

    public static Rac1TaxiPoint Lerp(Rac1TaxiPoint a, Rac1TaxiPoint b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}

public sealed record Rac1TaxiFlight(
    Rac1MetropolisTaxiStop From,
    Rac1TaxiPoint Position,
    Rac1TaxiPoint Arrival,
    double Progress,
    bool JustArrived);

/// <summary>
/// A source-backed, engine-neutral Kerwan taxi session. PVar-backed retail
/// paths advance using the original fixed-step seek recurrence; synthetic
/// fixtures retain provisional linear samples. Explicit boarding, player
/// attachment, camera and mission admission remain provisional host policy.
/// </summary>
public sealed class Rac1MetropolisTaxiSession
{
    public const int NativeLevelId = 3;
    public const double BoardingRadius = 5.5;
    public const double ProvisionalPathSamplesPerSecond = 60;
    public const string OutboundPathFormat = "rac1-metropolis-outbound-native-xyz-v1";
    public const string ReturnPathFormat = "rac1-metropolis-return-native-xyz-v1";
    public const string ApproachPathFormat = "rac1-metropolis-approach-native-xyz-v1";
    public const string DeparturePathFormat = "rac1-metropolis-departure-native-xyz-v1";
    public const string TriggerVolumeFormat = "rac1-metropolis-trigger-native-xyz-v1";

    // Both class-1012 taxi paths form authored round trips. Their outbound
    // endpoints have no safe walkable dock: passengers return before exiting.
    // Instance 921 is the parked destination of the two class-816 routes.
    private static readonly (int Index, int Class, int OutboundOffset, int ReturnOffset, int ApproachOffset, int DepartureOffset, int TriggerOffset, string Label)[] Routes =
    [
        (919, 816, 0xa8, -1, 0x94, 0xbc, 0x80, "Ride the flying taxi"),
        (920, 816, 0xa8, -1, 0x94, 0xbc, 0x80, "Ride the flying taxi"),
        (921, 816, -1, -1, -1, -1, -1, "Parked taxi"),
        (961, 1012, 0x94, 0x90, -1, -1, -1, "Ride the shuttle"),
        (962, 1012, 0x94, 0x90, -1, -1, -1, "Ride the shuttle"),
    ];

    public enum TrafficPhase { WaitingForHero, Approaching, Docked, Carrying, ParkedAtDestination, Departing }
    public sealed record TrafficFrame(int InstanceIndex, Rac1TaxiPoint Position, TrafficPhase Phase);

    private readonly List<Rac1MetropolisTaxiStop> _stops;
    private readonly Dictionary<int, (TrafficPhase Phase, double Elapsed)> _traffic = [];
    private readonly Dictionary<int, Rac1TaxiNativeMotion> _nativeTraffic = [];
    private Rac1TaxiNativeMotion? _nativeFlight;
    private bool _nativeReturning;
    private Rac1MetropolisTaxiStop? _riding;
    private double _elapsed;
    private bool _arrivalReported;
    private double _boardingCooldown;

    private Rac1MetropolisTaxiSession(List<Rac1MetropolisTaxiStop> stops)
    {
        _stops = stops;
        foreach (var stop in _stops.Where(s => s.ApproachPath is { Count: > 1 }))
            _traffic.Add(stop.InstanceIndex, (TrafficPhase.WaitingForHero, 0));
    }

    public IReadOnlyList<Rac1MetropolisTaxiStop> Stops => _stops;
    public bool IsRiding => _riding is not null;
    public bool IsReturning => _riding is not null && _nativeReturning;
    public Rac1MetropolisTaxiStop? Riding => _riding;

    /// <summary>
    /// Import-time source-game enrichment: the runtime model keeps only
    /// authored waypoint coordinates, serialized in their original native
    /// XYZ order. The session later converts Z-up to OBP Y-up. No ISO paths
    /// or source file handles are required in the generic Godot host.
    /// </summary>
    public static IReadOnlyList<RuntimeDynamicObject> AttachRetailOutboundPaths(
        byte[] gameplay, IReadOnlyList<RuntimeDynamicObject> objects)
    {
        var paths = Rac1Paths.Read(gameplay);
        var output = new List<RuntimeDynamicObject>(objects.Count);
        foreach (var obj in objects)
        {
            var route = Routes.FirstOrDefault(r => r.Index == obj.InstanceIndex);
            if (route.Class == 0 || route.OutboundOffset < 0)
            {
                output.Add(obj);
                continue;
            }
            if (obj.NativeClassId != route.Class)
                throw new InvalidDataException($"Kerwan taxi {route.Index} native class changed.");

            byte[] pvar = obj.NativePayloads?
                .SingleOrDefault(p => p.Format == "rac1-pvar")?.Data
                ?? throw new InvalidDataException($"Kerwan taxi {route.Index} has no native PVar.");
            if (pvar.Length < route.OutboundOffset + 4)
                throw new InvalidDataException($"Kerwan taxi {route.Index} PVar is truncated.");
            int pathIndex = BinaryPrimitives.ReadInt32LittleEndian(
                pvar.AsSpan(route.OutboundOffset, 4));
            if (pathIndex < 0 || pathIndex >= paths.Count || paths[pathIndex].Length < 2)
                throw new InvalidDataException($"Kerwan taxi {route.Index} outbound path {pathIndex} is invalid.");

            var points = paths[pathIndex];
            var source = obj.Transform.Matrix;
            if (source.Length != 16 ||
                Math.Abs(source[12] - points[0].X) > 0.15 ||
                Math.Abs(source[13] - points[0].Z) > 0.15 ||
                Math.Abs(source[14] - points[0].Y) > 0.15)
                throw new InvalidDataException($"Kerwan taxi {route.Index} outbound path does not start at its Moby placement.");

            var native = new byte[4 + points.Length * 12];
            BinaryPrimitives.WriteInt32LittleEndian(native.AsSpan(0, 4), points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                int at = 4 + i * 12;
                BinaryPrimitives.WriteSingleLittleEndian(native.AsSpan(at, 4), points[i].X);
                BinaryPrimitives.WriteSingleLittleEndian(native.AsSpan(at + 4, 4), points[i].Y);
                BinaryPrimitives.WriteSingleLittleEndian(native.AsSpan(at + 8, 4), points[i].Z);
            }
            var added = (obj.NativePayloads ?? [])
                .Append(new RuntimeOpaquePayload(OutboundPathFormat, native)).ToList();
            if (route.ReturnOffset >= 0)
            {
                int returnIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    pvar.AsSpan(route.ReturnOffset, 4));
                if (returnIndex < 0 || returnIndex >= paths.Count ||
                    paths[returnIndex].Length < 2)
                    throw new InvalidDataException(
                        $"Kerwan taxi {route.Index} return path {returnIndex} is invalid.");
                var returning = paths[returnIndex];
                if (System.Numerics.Vector3.Distance(points[^1], returning[0]) > 0.15f ||
                    System.Numerics.Vector3.Distance(returning[^1], points[0]) > 0.15f)
                    throw new InvalidDataException(
                        $"Kerwan taxi {route.Index} return path does not close its authored flight.");
                var returnNative = new byte[4 + returning.Length * 12];
                BinaryPrimitives.WriteInt32LittleEndian(
                    returnNative.AsSpan(0, 4), returning.Length);
                for (int i = 0; i < returning.Length; i++)
                {
                    int at = 4 + i * 12;
                    BinaryPrimitives.WriteSingleLittleEndian(
                        returnNative.AsSpan(at, 4), returning[i].X);
                    BinaryPrimitives.WriteSingleLittleEndian(
                        returnNative.AsSpan(at + 4, 4), returning[i].Y);
                    BinaryPrimitives.WriteSingleLittleEndian(
                        returnNative.AsSpan(at + 8, 4), returning[i].Z);
                }
                added.Add(new RuntimeOpaquePayload(ReturnPathFormat, returnNative));
            }
            if (route.ApproachOffset >= 0)
            {
                int approachIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    pvar.AsSpan(route.ApproachOffset, 4));
                int departureIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    pvar.AsSpan(route.DepartureOffset, 4));
                if (approachIndex < 0 || departureIndex < 0 ||
                    approachIndex >= paths.Count || departureIndex >= paths.Count)
                    throw new InvalidDataException($"Kerwan taxi {route.Index} has invalid approach/departure indices.");
                var approach = paths[approachIndex];
                var departure = paths[departureIndex];
                if (approach.Length < 2 || departure.Length < 2 ||
                    System.Numerics.Vector3.Distance(approach[^1], points[0]) > .15f ||
                    System.Numerics.Vector3.Distance(departure[0], points[^1]) > .15f)
                    throw new InvalidDataException($"Kerwan taxi {route.Index} has disconnected approach/departure splines.");
                added.Add(new RuntimeOpaquePayload(ApproachPathFormat, SerializeRetailTaxiPath(approach)));
                added.Add(new RuntimeOpaquePayload(DeparturePathFormat, SerializeRetailTaxiPath(departure)));

                // Original class-816 idle state calls point_in_box(HERO, PVar+0x80).
                // Recover that exact authored cuboid rather than a fake radius.
                int cuboidIndex = BinaryPrimitives.ReadInt32LittleEndian(
                    pvar.AsSpan(route.TriggerOffset, 4));
                int block = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(24 * 4, 4));
                if (block < 0 || block > gameplay.Length - 16)
                    throw new InvalidDataException("Kerwan native cuboid block is invalid.");
                int cuboidCount = BinaryPrimitives.ReadInt32LittleEndian(gameplay.AsSpan(block, 4));
                if (cuboidIndex < 0 || cuboidIndex >= cuboidCount ||
                    cuboidIndex > (gameplay.Length - block - 16 - 128) / 128)
                    throw new InvalidDataException($"Kerwan taxi {route.Index} has an invalid trigger box.");
                int at = block + 16 + cuboidIndex * 128;
                float NativeFloat(int offset) => BinaryPrimitives.ReadSingleLittleEndian(gameplay.AsSpan(at + offset, 4));
                // Boxes 10 and 28 are axis-aligned in native XYZ.
                if (Math.Abs(NativeFloat(4)) > .001f ||
                    Math.Abs(NativeFloat(8)) > .001f ||
                    Math.Abs(NativeFloat(16)) > .001f ||
                    Math.Abs(NativeFloat(24)) > .001f ||
                    Math.Abs(NativeFloat(32)) > .001f ||
                    Math.Abs(NativeFloat(36)) > .001f)
                    throw new InvalidDataException($"Kerwan taxi {route.Index} uses an unsupported rotated cuboid.");
                var volume = new byte[24];
                float[] fields = [
                    NativeFloat(48), NativeFloat(52), NativeFloat(56),
                    Math.Abs(NativeFloat(0)), Math.Abs(NativeFloat(20)), Math.Abs(NativeFloat(40)),
                ];
                for (int i = 0; i < fields.Length; i++)
                    BinaryPrimitives.WriteSingleLittleEndian(volume.AsSpan(i * 4, 4), fields[i]);
                added.Add(new RuntimeOpaquePayload(TriggerVolumeFormat, volume));
            }
            output.Add(obj with { NativePayloads = added.ToArray() });
        }
        return output;
    }

    private static byte[] SerializeRetailTaxiPath(IReadOnlyList<System.Numerics.Vector3> points)
    {
        var data = new byte[4 + points.Count * 12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0, 4), points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            int at = 4 + 12 * i;
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at, 4), points[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 4, 4), points[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 8, 4), points[i].Z);
        }
        return data;
    }

    private static Rac1TaxiPoint[]? ReadNativeTaxiPath(RuntimeDynamicObject obj, string format)
    {
        var data = obj.NativePayloads?.SingleOrDefault(p => p.Format == format)?.Data;
        if (data is null || data.Length < 28) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
        if (count is < 2 or > 8192 || data.Length != 4 + count * 12) return null;
        var path = new Rac1TaxiPoint[count];
        for (int i = 0; i < count; i++)
        {
            int at = 4 + 12 * i;
            double x = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at, 4));
            double y = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 8, 4));
            double z = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at + 4, 4));
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                return null;
            path[i] = new Rac1TaxiPoint(x, y, z);
        }
        return path;
    }

    public static Rac1MetropolisTaxiSession? TryCreate(RuntimeWorld world)
    {
        if (world.Game != ObpSourceGame.Rac1 || world.LevelId != NativeLevelId)
            return null;

        var objects = world.DynamicObjects ?? [];
        var stops = new List<Rac1MetropolisTaxiStop>(Routes.Length);
        foreach (var route in Routes)
        {
            var matches = objects.Where(o => o.InstanceIndex == route.Index).ToArray();
            if (matches.Length != 1 || matches[0].NativeClassId != route.Class ||
                matches[0].Transform.Matrix.Length != 16)
                return null;
            var obj = matches[0];
            var matrix = obj.Transform.Matrix;
            var position = new Rac1TaxiPoint(matrix[12], matrix[13], matrix[14]);
            Rac1TaxiPoint[] outbound = [];
            if (route.OutboundOffset >= 0)
            {
                var bytes = obj.NativePayloads?
                    .SingleOrDefault(p => p.Format == OutboundPathFormat)?.Data;
                if (bytes is null || bytes.Length < 28)
                    return null;
                int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
                if (count is < 2 or > 8192 || bytes.Length != 4 + count * 12)
                    return null;
                outbound = new Rac1TaxiPoint[count];
                for (int i = 0; i < count; i++)
                {
                    int at = 4 + i * 12;
                    double x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4));
                    double nativeY = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 4, 4));
                    double nativeZ = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8, 4));
                    if (!double.IsFinite(x) || !double.IsFinite(nativeY) || !double.IsFinite(nativeZ))
                        return null;
                    outbound[i] = new Rac1TaxiPoint(x, nativeZ, nativeY);
                }
                if (outbound[0].DistanceSquared(position) > 0.15 * 0.15)
                    return null;
            }

            Rac1TaxiPoint[] returning = [];
            if (route.ReturnOffset >= 0)
            {
                var bytes = obj.NativePayloads?
                    .SingleOrDefault(p => p.Format == ReturnPathFormat)?.Data;
                if (bytes is null || bytes.Length < 28)
                    return null;
                int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
                if (count is < 2 or > 8192 || bytes.Length != 4 + count * 12)
                    return null;
                returning = new Rac1TaxiPoint[count];
                for (int i = 0; i < count; i++)
                {
                    int at = 4 + i * 12;
                    double x = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4));
                    double nativeY = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 4, 4));
                    double nativeZ = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at + 8, 4));
                    if (!double.IsFinite(x) || !double.IsFinite(nativeY) || !double.IsFinite(nativeZ))
                        return null;
                    returning[i] = new Rac1TaxiPoint(x, nativeZ, nativeY);
                }
                if (returning[0].DistanceSquared(outbound[^1]) > 0.15 * 0.15 ||
                    returning[^1].DistanceSquared(position) > 0.15 * 0.15)
                    return null;
            }

            Rac1TaxiPoint[] approach = [];
            Rac1TaxiPoint[] departure = [];
            Rac1TaxiTriggerVolume? trigger = null;
            if (route.ApproachOffset >= 0 &&
                obj.NativePayloads?.Any(p => p.Format == ApproachPathFormat) == true)
            {
                approach = ReadNativeTaxiPath(obj, ApproachPathFormat) ?? [];
                departure = ReadNativeTaxiPath(obj, DeparturePathFormat) ?? [];
                var data = obj.NativePayloads?
                    .SingleOrDefault(p => p.Format == TriggerVolumeFormat)?.Data;
                if (approach.Length < 2 || departure.Length < 2 || data is not { Length: 24 } ||
                    approach[^1].DistanceSquared(position) > .15 * .15 ||
                    departure[0].DistanceSquared(outbound[^1]) > .15 * .15)
                    return null;
                float F(int i) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(4 * i, 4));
                float cx = F(0), cy = F(1), cz = F(2);
                float hx = F(3), hy = F(4), hz = F(5);
                if (!float.IsFinite(cx) || !float.IsFinite(cy) || !float.IsFinite(cz) ||
                    hx <= 0 || hy <= 0 || hz <= 0 ||
                    !float.IsFinite(hx) || !float.IsFinite(hy) || !float.IsFinite(hz))
                    return null;
                trigger = new Rac1TaxiTriggerVolume(
                    new Rac1TaxiPoint(cx, cz, cy), new Rac1TaxiPoint(hx, hz, hy));
            }

            // Class 1012 state 0 reads the authored starting end from
            // PVar+0x80. Its first selected route is +0x90+(side*4).
            // Retain the source-dock host placement for now: i961's native
            // initial end is airborne without a supported landing surface.
            var pvar = obj.NativePayloads?
                .SingleOrDefault(p => p.Format == "rac1-pvar")?.Data;
            int nativeSide = -1;
            if (route.Class == 1012 && pvar is { Length: > 0x80 })
            {
                nativeSide = pvar[0x80];
                if (nativeSide is not (0 or 1))
                    return null;
            }
            // Native class-1012 state 0 chooses an *initial route side*
            // using PVar+0x80 and the path indices at +0x90/+0x94.
            // Its state-1 nearest-end/reposition selector is independent
            // of the current OBP single-source-dock safety policy.
            Rac1TaxiNativeShuttleDock? nativeDock = null;
            if (route.Class == 1012 && nativeSide is 0 or 1 &&
                pvar is { Length: >= 0xa4 } dockPvar &&
                returning.Length > 1 && outbound.Length > 1)
            {
                int cuboid = BinaryPrimitives.ReadInt32LittleEndian(
                    dockPvar.AsSpan(0xa0, 4));
                if (cuboid < -1)
                    return null;
                nativeDock = new Rac1TaxiNativeShuttleDock(
                    returning, outbound, nativeSide, cuboid);
            }
            // The source class-816 bob/tilt helper uses PVar +0xec/0xf0/0xf4.
            // Class-1012 uses +0xac/+0xb0/+0xb4. The originally authored
            // phase values must survive the retail import (synthetic worlds
            // lacking PVars intentionally do not animate).
            int firstPhaseOffset = route.Class == 816 ? 0xec : 0xac;
            Rac1TaxiHoverPhases? hoverPhases = null;
            if (pvar is { } sourcePvar &&
                sourcePvar.Length >= firstPhaseOffset + 12)
            {
                double height = BinaryPrimitives.ReadSingleLittleEndian(
                    sourcePvar.AsSpan(firstPhaseOffset, 4));
                double tilt = BinaryPrimitives.ReadSingleLittleEndian(
                    sourcePvar.AsSpan(firstPhaseOffset + 4, 4));
                double spin = BinaryPrimitives.ReadSingleLittleEndian(
                    sourcePvar.AsSpan(firstPhaseOffset + 8, 4));
                if (!double.IsFinite(height) || !double.IsFinite(tilt) ||
                    !double.IsFinite(spin))
                    return null;
                hoverPhases = new Rac1TaxiHoverPhases(height, tilt, spin);
            }

            Rac1TaxiNativeHud? nativeHud = null;
            if (route.Class == 1012 && pvar is { Length: >= 0xa0 } hudPvar)
            {
                // Native updater 0x002dcf34 toggles +0xbc (side) before
                // selecting +0x98 + 4*side and requesting the HUD message.
                int zero = BinaryPrimitives.ReadInt32LittleEndian(
                    hudPvar.AsSpan(0x98, 4));
                int one = BinaryPrimitives.ReadInt32LittleEndian(
                    hudPvar.AsSpan(0x9c, 4));
                if (zero is < 1 or > 4 || one is < 1 or > 4)
                    return null;
                nativeHud = new Rac1TaxiNativeHud(zero, one);
            }

            // The class-816 state-6 clearance gate reads its currently
            // selected PVar +0xbc+(slot*4) exit selector, and uses a
            // distinct 5-unit planar rule for a -1 (pathless) slot.
            // The host currently admits only authored slot zero.
            int? firstDepartureSelector = null;
            IReadOnlyList<int>? allDepartureSelectors = null;
            if (route.Class == 816 && route.DepartureOffset >= 0 &&
                pvar is { Length: >= 0xd0 } departurePvar)
            {
                int[] selectors = new int[5];
                for (int slot = 0; slot < selectors.Length; slot++)
                {
                    selectors[slot] = BinaryPrimitives.ReadInt32LittleEndian(
                        departurePvar.AsSpan(route.DepartureOffset + 4 * slot, 4));
                    if (selectors[slot] < -1)
                        return null;
                }
                firstDepartureSelector = selectors[0];
                allDepartureSelectors = selectors;
            }

            // Real authored PVars prove the original speed/index fields
            // are present. Synthetic regression worlds omit this payload.
            bool nativeSeek = route.OutboundOffset >= 0 &&
                pvar is { Length: >= 0xc0 };

            stops.Add(new Rac1MetropolisTaxiStop(
                route.Index, route.Class, position, outbound, route.Label, returning,
                approach, departure, trigger, nativeSide, nativeSeek,
                hoverPhases, nativeHud, firstDepartureSelector,
                allDepartureSelectors, nativeDock,
                nativeHud.HasValue ? new Rac1TaxiNativeHudElementLease() : null));
        }

        return new Rac1MetropolisTaxiSession(stops);
    }

    /// <summary>
    /// Original class-816 routine: trigger cuboid -> approach path (+0x94)
    /// -> await passenger -> outbound (+0xa8) -> wait for clearance
    /// -> shared departure path (+0xbc). Rendering and camera are host-owned.
    /// Native movement accelerates along the curve; sample rate is provisional.
    /// </summary>
    public IReadOnlyList<TrafficFrame> TickTraffic(Rac1TaxiPoint hero, double delta)
    {
        if (!double.IsFinite(delta) || delta < 0)
            throw new ArgumentOutOfRangeException(nameof(delta));
        var frames = new List<TrafficFrame>(_traffic.Count);
        foreach (var stop in _stops.Where(s => s.ApproachPath is { Count: > 1 }))
        {
            var state = _traffic[stop.InstanceIndex];
            var phase = state.Phase;
            double elapsed = state.Elapsed;
            var position = stop.Position;
            switch (phase)
            {
                case TrafficPhase.WaitingForHero:
                    position = stop.ApproachPath![0];
                    if (!IsRiding && stop.ApproachTrigger?.Contains(hero) == true)
                    {
                        phase = TrafficPhase.Approaching;
                        elapsed = 0;
                        if (stop.NativeSeekEvidenced)
                            _nativeTraffic[stop.InstanceIndex] =
                                new Rac1TaxiNativeMotion(stop.ApproachPath!);
                    }
                    break;
                case TrafficPhase.Approaching:
                    elapsed += delta;
                    if (stop.NativeSeekEvidenced &&
                        _nativeTraffic.TryGetValue(stop.InstanceIndex, out var approach))
                    {
                        approach.Advance(delta);
                        position = approach.Position;
                        if (approach.Arrived)
                        {
                            phase = TrafficPhase.Docked;
                            elapsed = 0;
                            position = stop.Position;
                            _nativeTraffic.Remove(stop.InstanceIndex);
                        }
                    }
                    else
                    {
                        position = SampleTrafficPath(stop.ApproachPath!, elapsed);
                        if (elapsed >= (stop.ApproachPath!.Count - 1) / ProvisionalPathSamplesPerSecond)
                        {
                            phase = TrafficPhase.Docked;
                            elapsed = 0;
                            position = stop.Position;
                        }
                    }
                    break;
                case TrafficPhase.Docked:
                    position = stop.Position;
                    break;
                case TrafficPhase.Carrying:
                    // The main flight update moves this taxi and its passenger.
                    position = stop.Position;
                    break;
                case TrafficPhase.ParkedAtDestination:
                    position = stop.OutboundPath[^1];
                    // Native class-816 state 6 checks the current slot:
                    // departure path: >15 in 3D, pathless -1: >5 in
                    // the native horizontal plane, then idle directly.
                    if (Rac1TaxiNativeDepartureClearance.Cleared(
                        position, hero, stop.NativeFirstDepartureSelector))
                    {
                        elapsed = 0;
                        if (stop.NativeFirstDepartureSelector == -1)
                        {
                            // State 6 changes its byte to idle state 1
                            // without moving the Moby this update. State
                            // 1 restores the idle path on the next tick.
                            phase = TrafficPhase.WaitingForHero;
                        }
                        else
                        {
                            phase = TrafficPhase.Departing;
                            if (stop.NativeSeekEvidenced)
                                _nativeTraffic[stop.InstanceIndex] =
                                    new Rac1TaxiNativeMotion(stop.DeparturePath!);
                        }
                    }
                    break;
                case TrafficPhase.Departing:
                    elapsed += delta;
                    if (stop.NativeSeekEvidenced &&
                        _nativeTraffic.TryGetValue(stop.InstanceIndex, out var departure))
                    {
                        departure.Advance(delta);
                        position = departure.Position;
                        if (departure.Arrived)
                        {
                            phase = TrafficPhase.WaitingForHero;
                            elapsed = 0;
                            position = stop.ApproachPath![0];
                            _nativeTraffic.Remove(stop.InstanceIndex);
                        }
                    }
                    else
                    {
                        position = SampleTrafficPath(stop.DeparturePath!, elapsed);
                        if (elapsed >= (stop.DeparturePath!.Count - 1) / ProvisionalPathSamplesPerSecond)
                        {
                            phase = TrafficPhase.WaitingForHero;
                            elapsed = 0;
                            position = stop.ApproachPath![0];
                        }
                    }
                    break;
            }
            _traffic[stop.InstanceIndex] = (phase, elapsed);
            if (phase != TrafficPhase.Carrying)
                frames.Add(new TrafficFrame(stop.InstanceIndex, position, phase));
        }
        return frames;
    }

    private static Rac1TaxiPoint SampleTrafficPath(
        IReadOnlyList<Rac1TaxiPoint> path, double elapsed)
    {
        double sample = Math.Clamp(elapsed * ProvisionalPathSamplesPerSecond, 0, path.Count - 1);
        int first = (int)sample;
        int second = Math.Min(path.Count - 1, first + 1);
        return Rac1TaxiPoint.Lerp(path[first], path[second], sample - first);
    }

    public TrafficPhase? PhaseOf(int index) =>
        _traffic.TryGetValue(index, out var state) ? state.Phase : null;

    public Rac1MetropolisTaxiStop? FindBoardable(Rac1TaxiPoint player)
    {
        if (IsRiding || _boardingCooldown > 0 ||
            !double.IsFinite(player.X) || !double.IsFinite(player.Y) ||
            !double.IsFinite(player.Z)) return null;
        return _stops
            .Where(stop => stop.OutboundPath.Count >= 2 &&
                (stop.ApproachPath is not { Count: > 1 } ||
                    PhaseOf(stop.InstanceIndex) == TrafficPhase.Docked) &&
                stop.Position.DistanceSquared(player) <= BoardingRadius * BoardingRadius)
            .OrderBy(stop => stop.Position.DistanceSquared(player))
            .FirstOrDefault();
    }

    public bool TryBoard(Rac1TaxiPoint player)
    {
        var boarding = FindBoardable(player);
        if (boarding is null) return false;
        _riding = boarding;
        _elapsed = 0;
        _arrivalReported = false;
        _nativeFlight = boarding.NativeSeekEvidenced
            ? new Rac1TaxiNativeMotion(boarding.OutboundPath) : null;
        _nativeReturning = false;
        if (_traffic.ContainsKey(boarding.InstanceIndex))
            _traffic[boarding.InstanceIndex] = (TrafficPhase.Carrying, 0);
        return true;
    }

    /// <summary>
    /// Fail-safe cancellation for host interruption, death or world teardown.
    /// Native taxi camera/player detachment has not yet been ported, so this
    /// restores the session to its authored admission state without a cooldown.
    /// </summary>
    public void AbortRide()
    {
        var cancelled = _riding;
        _riding = null;
        _elapsed = 0;
        _arrivalReported = false;
        _boardingCooldown = 0;
        _nativeFlight = null;
        _nativeReturning = false;
        // Do not rewind a different cab while cancelling a passenger ride.
        if (cancelled is { ApproachPath.Count: > 1 })
        {
            _nativeTraffic.Remove(cancelled.InstanceIndex);
            _traffic[cancelled.InstanceIndex] = (TrafficPhase.WaitingForHero, 0);
        }
    }

    public void Reset()
    {
        AbortRide();
        _nativeTraffic.Clear();
        foreach (var stop in _stops)
        {
            // Class-1012 native state zero discards PVar +0xa4 even when
            // a previous HUD element serial was allocated by the retail engine.
            stop.NativeHudElementLease?.ResetOnStateZero();
            if (stop.ApproachPath is { Count: > 1 })
                _traffic[stop.InstanceIndex] = (TrafficPhase.WaitingForHero, 0);
        }
    }

    public Rac1TaxiFlight? Tick(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        if (_riding is not { } taxi)
        {
            _boardingCooldown = Math.Max(0, _boardingCooldown - deltaSeconds);
            return null;
        }

        if (_nativeFlight is { } native)
            return TickNativePassengerFlight(taxi, native, deltaSeconds);

        var outbound = taxi.OutboundPath;
        var returning = taxi.ReturnPath is { Count: > 1 } ? taxi.ReturnPath : null;
        double outwardDuration = (outbound.Count - 1) / ProvisionalPathSamplesPerSecond;
        double duration = outwardDuration +
            (returning is null ? 0 : (returning.Count - 1) / ProvisionalPathSamplesPerSecond);
        _elapsed = Math.Min(duration, _elapsed + deltaSeconds);
        bool flyingHome = returning is not null && _elapsed >= outwardDuration;
        var path = flyingHome ? returning! : outbound;
        double legElapsed = flyingHome ? _elapsed - outwardDuration : _elapsed;
        double sample = Math.Min(path.Count - 1, legElapsed * ProvisionalPathSamplesPerSecond);
        int first = Math.Min(path.Count - 1, (int)sample);
        int second = Math.Min(path.Count - 1, first + 1);
        double fraction = sample - Math.Floor(sample);
        var position = Rac1TaxiPoint.Lerp(path[first], path[second], fraction);
        bool justArrived = _elapsed >= duration && !_arrivalReported;
        if (justArrived) _arrivalReported = true;

        return new Rac1TaxiFlight(
            taxi, position, returning is null ? outbound[^1] : returning[^1],
            _elapsed / duration, justArrived);
    }

    private Rac1TaxiFlight TickNativePassengerFlight(
        Rac1MetropolisTaxiStop taxi, Rac1TaxiNativeMotion native, double deltaSeconds)
    {
        native.Advance(deltaSeconds);
        var returning = taxi.ReturnPath is { Count: > 1 } ? taxi.ReturnPath : null;
        if (native.Arrived && returning is not null && !_nativeReturning)
        {
            _nativeFlight = new Rac1TaxiNativeMotion(returning);
            _nativeReturning = true;
            native = _nativeFlight;
        }

        bool complete = native.Arrived &&
            (returning is null || _nativeReturning);
        bool justArrived = complete && !_arrivalReported;
        if (justArrived)
            _arrivalReported = true;
        double progress = returning is null ? native.Progress :
            _nativeReturning ? 0.5 + native.Progress * 0.5 :
            native.Progress * 0.5;

        return new Rac1TaxiFlight(taxi, native.Position,
            returning is null ? taxi.OutboundPath[^1] : returning[^1],
            progress, justArrived);
    }

    public void CompleteArrival()
    {
        Rac1MetropolisTaxiStop? stop = _riding;
        if (stop is not null && _traffic.ContainsKey(stop.InstanceIndex))
            _traffic[stop.InstanceIndex] = (TrafficPhase.ParkedAtDestination, 0);
        _riding = null;
        _elapsed = 0;
        _arrivalReported = false;
        _nativeFlight = null;
        _nativeReturning = false;
        // Native class-1012 state 3 starts frame_scale_i(15) when a
        // route ends. Class-816's 1.25-second debouncing is still host policy.
        _boardingCooldown = stop?.NativeClassId == 1012 ? 15d / 60d : 1.25;
    }
}
