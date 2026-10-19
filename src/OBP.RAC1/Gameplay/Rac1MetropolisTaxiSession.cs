using System.Buffers.Binary;
using OBP.RAC1.Level;
using OBP.Runtime;

namespace OBP.RAC1.Gameplay;

/// <summary>
/// Kerwan taxi placements and outbound retail flight splines. Class 816 and
/// 1012 use different PVar layouts. Flight timing and activation are separate
/// questions; the spline geometry comes from native LEVEL3 gameplay data.
/// </summary>
public sealed record Rac1MetropolisTaxiStop(
    int InstanceIndex,
    int NativeClassId,
    Rac1TaxiPoint Position,
    IReadOnlyList<Rac1TaxiPoint> OutboundPath,
    string DestinationLabel,
    IReadOnlyList<Rac1TaxiPoint>? ReturnPath = null);

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
/// A source-backed, engine-neutral Kerwan taxi prototype. Each flight traces
/// the authored spline; the single-sample-per-tick playback rate, explicit
/// boarding and player/camera interaction remain provisional host policy.
/// </summary>
public sealed class Rac1MetropolisTaxiSession
{
    public const int NativeLevelId = 3;
    public const double BoardingRadius = 5.5;
    public const double ProvisionalPathSamplesPerSecond = 60;
    public const string OutboundPathFormat = "rac1-metropolis-outbound-native-xyz-v1";
    public const string ReturnPathFormat = "rac1-metropolis-return-native-xyz-v1";

    // Both class-1012 taxi paths form authored round trips. Their outbound
    // endpoints have no safe walkable dock: passengers return before exiting.
    // Instance 921 is the parked destination of the two class-816 routes.
    private static readonly (int Index, int Class, int OutboundOffset, int ReturnOffset, string Label)[] Routes =
    [
        (919, 816, 0xa8, -1, "Ride the flying taxi"),
        (920, 816, 0xa8, -1, "Ride the flying taxi"),
        (921, 816, -1, -1, "Parked taxi"),
        (961, 1012, 0x94, 0x90, "Ride the round-trip taxi"),
        (962, 1012, 0x94, 0x90, "Ride the round-trip taxi"),
    ];

    private readonly List<Rac1MetropolisTaxiStop> _stops;
    private Rac1MetropolisTaxiStop? _riding;
    private double _elapsed;
    private bool _arrivalReported;
    private double _boardingCooldown;

    private Rac1MetropolisTaxiSession(List<Rac1MetropolisTaxiStop> stops) =>
        _stops = stops;

    public IReadOnlyList<Rac1MetropolisTaxiStop> Stops => _stops;
    public bool IsRiding => _riding is not null;
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
            output.Add(obj with { NativePayloads = added.ToArray() });
        }
        return output;
    }

    public static Rac1MetropolisTaxiSession? TryCreate(RuntimeWorld world)
    {
        if (world.Game != "rac1" || world.LevelId != NativeLevelId)
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

            stops.Add(new Rac1MetropolisTaxiStop(
                route.Index, route.Class, position, outbound, route.Label, returning));
        }

        return new Rac1MetropolisTaxiSession(stops);
    }

    public Rac1MetropolisTaxiStop? FindBoardable(Rac1TaxiPoint player)
    {
        if (IsRiding || _boardingCooldown > 0) return null;
        return _stops
            .Where(stop => stop.OutboundPath.Count >= 2 &&
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
        return true;
    }

    public void Reset()
    {
        _riding = null;
        _elapsed = 0;
        _arrivalReported = false;
        _boardingCooldown = 0;
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

    public void CompleteArrival()
    {
        _riding = null;
        _elapsed = 0;
        _arrivalReported = false;
        _boardingCooldown = 1.25;
    }
}
