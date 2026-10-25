using System.Text.Json;

namespace OBP.Fusion;

/// <summary>Authored OBP-only greybox layout. Geometry here is never imported retail material.</summary>
public sealed record FusionLabDefinition
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public required string Id { get; init; }
    public required FusionLabPoint Spawn { get; init; }
    public required IReadOnlyList<FusionLabRoom> Rooms { get; init; }
    public required IReadOnlyList<FusionLabConnection> Connections { get; init; }
    public IReadOnlyList<FusionLabStation> Stations { get; init; } = Array.Empty<FusionLabStation>();
    public IReadOnlyList<ActorPlacement> Actors { get; init; } = Array.Empty<ActorPlacement>();

    public static FusionLabDefinition Parse(string json)
    {
        var definition = JsonSerializer.Deserialize<FusionLabDefinition>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new JsonException("Fusion Lab definition is null.");
        definition.Validate();
        return definition;
    }

    public void Validate()
    {
        if (Version != CurrentVersion || string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("Unsupported Fusion Lab schema or missing lab id.");
        if (Rooms is null || Rooms.Count < 2 || Connections is null || Stations is null || Actors is null)
            throw new ArgumentException("Fusion Lab needs connected rooms and station lists.");

        var rooms = new Dictionary<string, FusionLabRoom>(StringComparer.Ordinal);
        foreach (var room in Rooms)
        {
            if (string.IsNullOrWhiteSpace(room.Id) || !rooms.TryAdd(room.Id, room))
                throw new ArgumentException($"Duplicate or empty room id '{room.Id}'.");
            if (!double.IsFinite(room.CenterX) || !double.IsFinite(room.CenterZ) ||
                Math.Abs(room.CenterX) > 2000 || Math.Abs(room.CenterZ) > 2000 ||
                !double.IsFinite(room.Width) || !double.IsFinite(room.Depth) ||
                room.Width < 5 || room.Depth < 5 || room.Width > 200 || room.Depth > 200 ||
                room.Tint is null || room.Tint.Length != 3 ||
                room.Tint.Any(c => !double.IsFinite(c) || c < 0 || c > 1))
                throw new ArgumentException($"Room '{room.Id}' has invalid bounds/tint.");
        }
        if (Spawn is null || !PointWithinAnyRoom(Spawn, rooms.Values) ||
            !double.IsFinite(Spawn.Y) || Spawn.Y < 0 || Spawn.Y > 10)
            throw new ArgumentException("Lab spawn must lie inside the floor of an authored room.");

        var edges = new HashSet<string>(StringComparer.Ordinal);
        var adjacency = rooms.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var connection in Connections)
        {
            if (connection.From == connection.To || !rooms.TryGetValue(connection.From, out var from) ||
                !rooms.TryGetValue(connection.To, out var to) ||
                !edges.Add(connection.From + ">" + connection.To) ||
                !double.IsFinite(connection.OpeningWidth) ||
                connection.OpeningWidth < 3 || connection.OpeningWidth > Math.Min(from.Width, to.Width) - 1 ||
                Math.Abs((from.CenterZ + from.Depth / 2) - (to.CenterZ - to.Depth / 2)) > 0.001 ||
                Math.Abs(from.CenterX - to.CenterX) > 0.001)
                throw new ArgumentException($"Invalid northbound connection '{connection.From}' > '{connection.To}'.");
            adjacency[from.Id].Add(to.Id);
            adjacency[to.Id].Add(from.Id);
        }

        // A linear north-south layout can be added to without special host code.
        // Reject ambiguous multiple portals on the same wall until that geometry is implemented.
        foreach (var room in Rooms)
        {
            if (Connections.Count(c => c.From == room.Id) > 1 ||
                Connections.Count(c => c.To == room.Id) > 1)
                throw new ArgumentException($"Room '{room.Id}' has overlapping north/south portals.");
        }

        var reached = new HashSet<string>(StringComparer.Ordinal) { Rooms[0].Id };
        var queue = new Queue<string>();
        queue.Enqueue(Rooms[0].Id);
        while (queue.Count > 0)
        {
            foreach (var neighbour in adjacency[queue.Dequeue()])
                if (reached.Add(neighbour)) queue.Enqueue(neighbour);
        }
        if (reached.Count != Rooms.Count)
            throw new ArgumentException("Fusion Lab contains unreachable rooms.");

        var stationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var station in Stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id) || !stationIds.Add(station.Id) ||
                !rooms.TryGetValue(station.RoomId, out var room) ||
                station.Position is null || !PointWithinAnyRoom(station.Position, new[] { room }) ||
                !double.IsFinite(station.Position.Y) ||
                string.IsNullOrWhiteSpace(station.Label))
                throw new ArgumentException($"Invalid or duplicate station '{station.Id}'.");
            station.Source?.Validate();
            if (station.Source is not null && string.IsNullOrWhiteSpace(station.AdapterId))
                throw new ArgumentException($"Source station '{station.Id}' needs an explicit adapter.");
        }
        var actorIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var actor in Actors)
        {
            actor.Validate();
            var matrix = actor.Transform.Matrix;
            if (!actorIds.Add(actor.Id) || !PointWithinAnyRoom(new(matrix[12], matrix[13], matrix[14]), Rooms))
                throw new ArgumentException($"Actor '{actor.Id}' is duplicated or outside the authored rooms.");
        }
    }

    private static bool PointWithinAnyRoom(FusionLabPoint point, IEnumerable<FusionLabRoom> rooms) =>
        double.IsFinite(point.X) && double.IsFinite(point.Z) &&
        rooms.Any(room => Math.Abs(point.X - room.CenterX) < room.Width / 2 - 0.5 &&
                          Math.Abs(point.Z - room.CenterZ) < room.Depth / 2 - 0.5);
}

public sealed record FusionLabPoint(double X, double Y, double Z);
public sealed record FusionLabRoom(string Id, string Label, double CenterX, double CenterZ,
    double Width, double Depth, double[] Tint);
public sealed record FusionLabConnection(string From, string To, double OpeningWidth);

/// <summary>
/// An authored placement hook, not an implicit asset spawn. An unsupported
/// adapter or missing native source produces an unavailable station sign.
/// </summary>
public sealed record FusionLabStation(
    string Id, string RoomId, string Label, FusionLabPoint Position,
    FusionSourceIdentity? Source = null, string? AdapterId = null,
    string? ResetKey = null, IReadOnlyDictionary<string, string>? Parameters = null);
