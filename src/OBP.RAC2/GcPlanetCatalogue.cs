namespace OBP.RAC2;

/// <summary>What a Going Commando level <em>is</em> — only <see cref="Planet"/> / <see cref="Hub"/> levels are the walkable showcase targets.</summary>
public enum GcLevelKind
{
    /// <summary>A normal walkable planet with a native ship-park spawn.</summary>
    Planet,

    /// <summary>Walkable, but the ship spawn is a placeholder (Aranos airship / the Aranos return).</summary>
    Hub,

    /// <summary>Slim Cognito's Ship Shack — the walkable vendor asteroid.</summary>
    Vendor,

    /// <summary>On-rails / spherical-gravity space-combat level — loads, but not a walking showcase.</summary>
    SpaceCombat,

    /// <summary>A starfield / cutscene stub with little or no collision.</summary>
    Scene,

    /// <summary>Imports, but the level identity / instance placement is not yet resolved (LEVEL14).</summary>
    Unresolved,
}

/// <summary>
/// The Going Commando planet / location catalogue, transcribed from
/// <c>research/GC_PLANET_NAMES.md</c>. Names are the decoded global-string-bank
/// help messages — planet names at help ids <c>2885 + planet_name_index</c>, the
/// "&lt;location&gt;, Planet &lt;planet&gt;" infobot pairs at ids <c>4592+</c> —
/// and the level→name rule is <c>planet_name_index == level_id</c> for the main
/// levels (LEVEL0–LEVEL20), with <c>LEVEL21.WAD</c> self-reporting engine id 30
/// and the ELF special table mapping id 30 → planet 0 (Aranos).
///
/// <para>
/// This is the debug planet selector's data source. It is <b>not</b> a second
/// hand-maintained truth table for the loader: <see cref="GcWorldImport"/> takes
/// any level id and never consults this list. The <see cref="GcLevelKind"/>
/// classification comes from the deterministic level probe (ship spawn present,
/// <c>is_spherical_world</c>, geometry/collision counts) recorded in
/// <c>docs/GC_PLANET_HOPPING.md</c>.
/// </para>
/// </summary>
public static class GcPlanetCatalogue
{
    public sealed record Entry(
        int LevelId,
        string Planet,
        string Location,
        int PlanetNameStringId,
        GcLevelKind Kind)
    {
        public string ContainerFile => LevelId == 21 ? "/G/LEVEL21.WAD" : $"/G/LEVEL{LevelId}.WAD";

        /// <summary>"Endako — Megapolis" for planets; just the planet name when there is no distinct location.</summary>
        public string DisplayName => Location.Length > 0 ? $"{Planet} — {Location}" : Planet;

        public bool Walkable => Kind is GcLevelKind.Planet or GcLevelKind.Hub or GcLevelKind.Vendor;
    }

    /// <summary>Every id the importer has been probed against, in engine order. Index 21 is <c>LEVEL21.WAD</c> (engine id 30).</summary>
    public static readonly IReadOnlyList<Entry> All =
    [
        new(0,  "Aranos",         "Floating Prison",     2885, GcLevelKind.Hub),
        new(1,  "Oozla",          "The Megacorp Outlet", 2886, GcLevelKind.Planet),
        new(2,  "Maktar Nebula",  "Maktar Resort",       2887, GcLevelKind.Planet),
        new(3,  "Endako",         "Megapolis",           2888, GcLevelKind.Planet),
        new(4,  "Barlow",         "Vukovar Canyon",      2889, GcLevelKind.Planet),
        new(5,  "Feltzin System", "Thug Rendezvous",     2890, GcLevelKind.SpaceCombat),
        new(6,  "Notak",          "Canal City",          2891, GcLevelKind.Planet),
        new(7,  "Siberius",       "Frozen Lab",          2892, GcLevelKind.Planet),
        new(8,  "Tabora",         "Mining Area",         2893, GcLevelKind.Planet),
        new(9,  "Dobbo",          "Testing Facility",    2894, GcLevelKind.Planet),
        new(10, "Hrugis Cloud",   "Deep Space Disposal", 2895, GcLevelKind.SpaceCombat),
        new(11, "Joba",           "Megacorp Games",      2896, GcLevelKind.Planet),
        new(12, "Todano",         "Megacorp Armory",     2897, GcLevelKind.Planet),
        new(13, "Boldan",         "Silver City",         2898, GcLevelKind.Planet),
        new(14, "Aranos",         "return (unresolved)", 2899, GcLevelKind.Unresolved),
        new(15, "Gorn",           "Thug Fleet",          2900, GcLevelKind.SpaceCombat),
        new(16, "Snivelak",       "Thug Headquarters",   2901, GcLevelKind.Planet),
        new(17, "Smolg",          "Distribution Center", 2902, GcLevelKind.Planet),
        new(18, "Damosel",        "Allgon City",         2903, GcLevelKind.Planet),
        new(19, "Grelbin",        "Tundor Wastes",       2904, GcLevelKind.Planet),
        new(20, "Yeedil",         "Protopet Factory",    2905, GcLevelKind.Planet),
        new(21, "Aranos",         "Floating Prison",     2885, GcLevelKind.Hub),
        new(22, "Feltzin System", "Space Arena",         2890, GcLevelKind.SpaceCombat),
        new(23, "Hrugis Cloud",   "Space Arena",         2895, GcLevelKind.SpaceCombat),
        new(24, "Ship Shack",     "Slim Cognito",        2909, GcLevelKind.Vendor),
        new(25, "Starfield",      "scene stub",          0,    GcLevelKind.Scene),
        new(26, "Gorn",           "Space Arena",         2900, GcLevelKind.SpaceCombat),
    ];

    /// <summary>
    /// The five acceptance-floor showcase worlds — maximally distinct biomes that
    /// also frame well from the native ship spawn: Oozla (swamp), Endako (metal
    /// vertical city), Tabora (rock desert), Siberius (ice / snow lab), Damosel
    /// (ornate domed canal city). Not a loader constraint — any id imports.
    /// </summary>
    public static readonly IReadOnlyList<int> ShowcaseLevelIds = [1, 3, 8, 7, 18];

    public static Entry? Find(int levelId) => All.FirstOrDefault(e => e.LevelId == levelId);

    /// <summary>Resolve "oozla", "endako", "3", "LEVEL8" etc. to a level id (case-insensitive). Null if nothing matches.</summary>
    public static int? Resolve(string token)
    {
        token = token.Trim();
        if (int.TryParse(token, out int id) && Find(id) is not null)
        {
            return id;
        }

        if (token.StartsWith("LEVEL", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[5..], out int lvl) && Find(lvl) is not null)
        {
            return lvl;
        }

        var byPlanet = All.FirstOrDefault(e =>
            string.Equals(e.Planet, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Location, token, StringComparison.OrdinalIgnoreCase));
        return byPlanet?.LevelId;
    }
}
