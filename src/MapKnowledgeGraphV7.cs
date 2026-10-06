namespace Sm0kiSoloCoach;

public sealed record MapGraphNodeV7(
    string Id,
    string Label,
    string Zone,
    IReadOnlyList<string> Aliases);

public sealed record MapGraphEdgeV7(
    string From,
    string To);

public sealed class MapGraphDefinitionV7
{
    public string Map { get; init; } = "";
    public IReadOnlyDictionary<string,MapGraphNodeV7> Nodes { get; init; } =
        new Dictionary<string,MapGraphNodeV7>();
    public IReadOnlyList<MapGraphEdgeV7> Edges { get; init; } =
        Array.Empty<MapGraphEdgeV7>();
}

public static class MapKnowledgeGraphV7
{
    private static readonly Dictionary<string,MapGraphDefinitionV7> Graphs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de_mirage"] = Build(
                "de_mirage",
                new[]
                {
                    N("tspawn","T Spawn","OTHER","t spawn"),
                    N("aramp","A Ramp","A","a ramp","ramp"),
                    N("palace","Palace","A","palace"),
                    N("asite","A Site","A","a site","triple","default"),
                    N("jungle","Jungle","A","jungle"),
                    N("mid","Mid","MID","mid","top mid"),
                    N("connector","Connector","MID","connector"),
                    N("window","Window","MID","window"),
                    N("underpass","Underpass","MID","underpass"),
                    N("short","Short / Cat","MID","short","cat"),
                    N("bapps","B Apps","B","b apps","apps"),
                    N("bsite","B Site","B","b site","bench","van"),
                    N("market","Market","B","market")
                },
                ("tspawn","aramp"),("aramp","asite"),("palace","asite"),
                ("tspawn","mid"),("mid","connector"),("connector","asite"),
                ("mid","window"),("mid","underpass"),("underpass","short"),
                ("mid","short"),("short","bsite"),("tspawn","bapps"),
                ("bapps","bsite"),("bsite","market"),("connector","jungle"),
                ("jungle","asite")),

            ["de_inferno"] = Build(
                "de_inferno",
                new[]
                {
                    N("tspawn","T Spawn","OTHER","t spawn"),
                    N("banana","Banana","B","banana","logs","car"),
                    N("bsite","B Site","B","b site"),
                    N("coffins","Coffins","B","coffins"),
                    N("ct","CT","B","ct"),
                    N("mid","Mid","MID","mid"),
                    N("secondmid","Second Mid","MID","second mid"),
                    N("apps","Apartments","A","apps","apartments"),
                    N("short","A Short","A","short"),
                    N("long","A Long","A","long","library"),
                    N("asite","A Site","A","a site","default","new box"),
                    N("pit","Pit","A","pit")
                },
                ("tspawn","banana"),("banana","bsite"),("bsite","coffins"),
                ("bsite","ct"),("tspawn","mid"),("mid","short"),
                ("mid","long"),("secondmid","apps"),("apps","asite"),
                ("short","asite"),("long","asite"),("asite","pit")),

            ["de_dust2"] = Build(
                "de_dust2",
                new[]
                {
                    N("tspawn","T Spawn","OTHER","t spawn"),
                    N("long","Long","A","long","long a"),
                    N("asite","A Site","A","a site","goose","ramp"),
                    N("short","Short / Cat","MID","short","cat"),
                    N("mid","Mid","MID","mid","xbox"),
                    N("lower","Lower Tunnels","MID","lower tunnel"),
                    N("upper","Upper Tunnels","B","upper tunnel","tunnels"),
                    N("bsite","B Site","B","b site","back plat"),
                    N("bdoors","B Doors","B","b doors","doors"),
                    N("ct","CT","MID","ct")
                },
                ("tspawn","long"),("long","asite"),("tspawn","mid"),
                ("mid","short"),("short","asite"),("mid","lower"),
                ("lower","upper"),("upper","bsite"),("bsite","bdoors"),
                ("bdoors","ct"),("mid","ct")),

            ["de_nuke"] = Build(
                "de_nuke",
                new[]
                {
                    N("lobby","Lobby","OTHER","lobby"),
                    N("hut","Hut","A","hut"),
                    N("squeaky","Squeaky","A","squeaky"),
                    N("asite","A Site","A","a site","rafters"),
                    N("main","Main","A","main"),
                    N("outside","Outside","MID","outside","yard"),
                    N("secret","Secret","B","secret"),
                    N("ramp","Ramp","B","ramp"),
                    N("bsite","B Site","B","b site"),
                    N("decon","Decon","B","decon"),
                    N("control","Control","B","control")
                },
                ("lobby","hut"),("hut","asite"),("lobby","squeaky"),
                ("squeaky","asite"),("main","asite"),("outside","main"),
                ("outside","secret"),("secret","bsite"),("lobby","ramp"),
                ("ramp","bsite"),("bsite","decon"),("bsite","control")),

            ["de_ancient"] = Build(
                "de_ancient",
                new[]
                {
                    N("tspawn","T Spawn","OTHER","t spawn"),
                    N("amain","A Main","A","a main"),
                    N("asite","A Site","A","a site"),
                    N("donut","Donut","MID","donut"),
                    N("mid","Mid","MID","mid"),
                    N("cave","Cave","B","cave"),
                    N("bramp","B Ramp","B","b ramp","ramp"),
                    N("bsite","B Site","B","b site","pillar"),
                    N("ct","CT","B","ct"),
                    N("temple","Temple","A","temple")
                },
                ("tspawn","amain"),("amain","asite"),("mid","donut"),
                ("donut","asite"),("tspawn","mid"),("tspawn","bramp"),
                ("bramp","cave"),("cave","bsite"),("bramp","bsite"),
                ("bsite","ct"),("asite","temple")),

            ["de_anubis"] = Build(
                "de_anubis",
                new[]
                {
                    N("amain","A Main","A","a main"),
                    N("asite","A Site","A","a site"),
                    N("mid","Mid","MID","mid"),
                    N("water","Water","MID","water"),
                    N("connector","Connector","MID","connector"),
                    N("bmain","B Main","B","b main"),
                    N("bsite","B Site","B","b site"),
                    N("heaven","Heaven","A","heaven"),
                    N("ct","CT","B","ct")
                },
                ("amain","asite"),("mid","connector"),("connector","asite"),
                ("mid","water"),("water","bsite"),("bmain","bsite"),
                ("bsite","ct"),("asite","heaven")),

            ["de_overpass"] = Build(
                "de_overpass",
                new[]
                {
                    N("fountain","Fountain","A","fountain"),
                    N("bathrooms","Bathrooms","A","bathrooms","toilets"),
                    N("long","Long A","A","long a","long"),
                    N("asite","A Site","A","a site","truck","bank"),
                    N("connector","Connector","MID","connector"),
                    N("water","Water","MID","water"),
                    N("shortb","Short B","B","short b","short"),
                    N("monster","Monster","B","monster"),
                    N("bsite","B Site","B","b site","pillar","barrels")
                },
                ("fountain","bathrooms"),("bathrooms","asite"),
                ("fountain","long"),("long","asite"),("connector","water"),
                ("water","shortb"),("shortb","bsite"),("monster","bsite")),

            ["de_vertigo"] = Build(
                "de_vertigo",
                new[]
                {
                    N("aramp","A Ramp","A","a ramp","ramp"),
                    N("asite","A Site","A","a site","sandbags"),
                    N("short","Short","A","short"),
                    N("mid","Mid","MID","mid"),
                    N("bstairs","B Stairs","B","b stairs","stairs"),
                    N("bsite","B Site","B","b site"),
                    N("elevator","Elevator","MID","elevator")
                },
                ("aramp","asite"),("short","asite"),("mid","short"),
                ("mid","elevator"),("mid","bstairs"),("bstairs","bsite")),

            ["de_train"] = Build(
                "de_train",
                new[]
                {
                    N("amain","A Main","A","a main"),
                    N("asite","A Site","A","a site","yard"),
                    N("ivy","Ivy","A","ivy"),
                    N("popdog","Popdog","MID","popdog"),
                    N("connector","Connector","MID","connector"),
                    N("bupper","B Upper","B","b upper","upper"),
                    N("blower","B Lower","B","b lower","lower"),
                    N("bsite","B Site","B","b site")
                },
                ("amain","asite"),("ivy","asite"),("popdog","asite"),
                ("connector","asite"),("connector","bsite"),
                ("bupper","bsite"),("blower","bsite"))
        };

    public static MapGraphDefinitionV7? Get(
        string map)
        => Graphs.TryGetValue(
            map ?? "",
            out var graph)
            ? graph
            : null;

    public static IReadOnlyList<MapGraphNodeV7> ResolveRoute(
        string map,
        string route)
    {
        var graph = Get(map);
        if (graph == null ||
            string.IsNullOrWhiteSpace(route))
            return Array.Empty<MapGraphNodeV7>();

        var normalized =
            Normalize(route);

        var found = graph.Nodes.Values
            .Select(node => new
            {
                Node = node,
                Index = node.Aliases
                    .Select(alias =>
                        normalized.IndexOf(
                            Normalize(alias),
                            StringComparison.OrdinalIgnoreCase))
                    .Where(x => x >= 0)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min()
            })
            .Where(x =>
                x.Index != int.MaxValue)
            .OrderBy(x => x.Index)
            .Select(x => x.Node)
            .DistinctBy(x => x.Id)
            .ToList();

        return found;
    }

    public static double ConnectivityScore(
        string map,
        string route)
    {
        var graph = Get(map);
        if (graph == null)
            return 50;

        var nodes =
            ResolveRoute(
                map,
                route);

        if (nodes.Count == 0)
            return 48;

        if (nodes.Count == 1)
            return 68;

        var connected = 0;

        for (var i = 0;
             i < nodes.Count - 1;
             i++)
        {
            if (AreConnected(
                    graph,
                    nodes[i].Id,
                    nodes[i + 1].Id))
                connected++;
        }

        var ratio =
            (double)connected /
            Math.Max(
                1,
                nodes.Count - 1);

        return Math.Clamp(
            45 +
            ratio * 50,
            30,
            95);
    }

    public static string RouteEvidence(
        string map,
        string route,
        IReadOnlyList<GroundTruthPathSample> path)
    {
        var graph = Get(map);

        if (graph == null ||
            path.Count == 0)
            return "UNKNOWN";

        var expected =
            ResolveRoute(
                map,
                route);

        if (expected.Count == 0)
            return "UNKNOWN";

        var observed =
            path
                .SelectMany(sample =>
                {
                    var text =
                        Normalize(
                            sample.PlaceName);

                    return graph.Nodes.Values
                        .Where(node =>
                            node.Aliases.Any(alias =>
                                text.Contains(
                                    Normalize(alias),
                                    StringComparison.OrdinalIgnoreCase)));
                })
                .Select(x => x.Id)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        if (observed.Count == 0)
            return "UNKNOWN";

        var expectedIds =
            expected
                .Select(x => x.Id)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var hits =
            expectedIds.Count(
                observed.Contains);

        if (hits >= 1)
            return "SUPPORTED";

        var expectedZones =
            expected
                .Select(x => x.Zone)
                .Where(x =>
                    x is "A" or "B" or "MID")
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var observedZones =
            path
                .Select(x => x.Zone)
                .Where(x =>
                    x is "A" or "B" or "MID")
                .ToList();

        if (expectedZones.Count == 1 &&
            observedZones.Count >= 3 &&
            !observedZones.Any(
                expectedZones.Contains))
            return "CONTRADICTED";

        return "UNKNOWN";
    }

    private static bool AreConnected(
        MapGraphDefinitionV7 graph,
        string a,
        string b)
        => graph.Edges.Any(edge =>
            (edge.From.Equals(
                 a,
                 StringComparison.OrdinalIgnoreCase) &&
             edge.To.Equals(
                 b,
                 StringComparison.OrdinalIgnoreCase)) ||
            (edge.From.Equals(
                 b,
                 StringComparison.OrdinalIgnoreCase) &&
             edge.To.Equals(
                 a,
                 StringComparison.OrdinalIgnoreCase)));

    private static MapGraphNodeV7 N(
        string id,
        string label,
        string zone,
        params string[] aliases)
        => new(
            id,
            label,
            zone,
            aliases);

    private static MapGraphDefinitionV7 Build(
        string map,
        IEnumerable<MapGraphNodeV7> nodes,
        params (string From,string To)[] edges)
    {
        var dict =
            nodes.ToDictionary(
                x => x.Id,
                StringComparer.OrdinalIgnoreCase);

        return new MapGraphDefinitionV7
        {
            Map = map,
            Nodes = dict,
            Edges = edges
                .Select(x =>
                    new MapGraphEdgeV7(
                        x.From,
                        x.To))
                .ToList()
        };
    }

    private static string Normalize(
        string value)
        => (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "");
}
