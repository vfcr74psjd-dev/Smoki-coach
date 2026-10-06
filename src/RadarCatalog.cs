using System.Drawing;

namespace Sm0kiSoloCoach;

public sealed record RadarDefinition(
    string Map,
    double PosX,
    double PosY,
    double Scale,
    bool Rotate,
    string RadarUrl,
    string? LowerRadarUrl = null,
    double? LowerBelowZ = null);

public sealed record SpawnProfile(
    bool Known,
    string Bias,
    string Label,
    float RadarX,
    float RadarY);

public static class RadarCatalog
{
    private const string Base = "https://raw.githubusercontent.com/MurkyYT/cs2-map-icons/main/images/radars/";

    private static readonly Dictionary<string, RadarDefinition> Maps =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de_ancient"] = new("de_ancient", -2953, 2164, 5.0, false, Base + "de_ancient_radar_psd.png"),
            ["de_anubis"] = new("de_anubis", -2796, 3328, 5.22, false, Base + "de_anubis_radar_psd.png"),
            ["de_dust2"] = new("de_dust2", -2476, 3239, 4.4, true, Base + "de_dust2_radar_psd.png"),
            ["de_inferno"] = new("de_inferno", -2087, 3870, 4.9, false, Base + "de_inferno_radar_psd.png"),
            ["de_mirage"] = new("de_mirage", -3230, 1713, 5.0, false, Base + "de_mirage_radar_psd.png"),
            ["de_nuke"] = new("de_nuke", -3453, 2887, 7.0, false,
                Base + "de_nuke_radar_psd.png", Base + "de_nuke_lower_radar_psd.png", -495),
            ["de_overpass"] = new("de_overpass", -4831, 1781, 5.2, false, Base + "de_overpass_radar_psd.png"),
            ["de_train"] = new("de_train", -2308, 2078, 4.082077, false,
                Base + "de_train_radar_psd.png", Base + "de_train_lower_radar_psd.png", -50),
            ["de_vertigo"] = new("de_vertigo", -3168, 1762, 4.0, false,
                Base + "de_vertigo_radar_psd.png", Base + "de_vertigo_lower_radar_psd.png", 11700)
        };


    // Official overview bomb-site markers, normalized to the 1024x1024 radar.
    // Anubis currently does not expose A/B site markers in its overview metadata,
    // so we intentionally leave it out instead of guessing.
    private static readonly Dictionary<string, (PointF A, PointF B)> BombSites =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de_ancient"] = (new PointF(0.31f, 0.25f), new PointF(0.80f, 0.40f)),
            ["de_dust2"] = (new PointF(0.80f, 0.16f), new PointF(0.21f, 0.12f)),
            ["de_inferno"] = (new PointF(0.81f, 0.69f), new PointF(0.49f, 0.22f)),
            ["de_mirage"] = (new PointF(0.54f, 0.76f), new PointF(0.23f, 0.28f)),
            ["de_nuke"] = (new PointF(0.58f, 0.48f), new PointF(0.58f, 0.58f)),
            ["de_overpass"] = (new PointF(0.55f, 0.23f), new PointF(0.70f, 0.31f)),
            ["de_train"] = (new PointF(0.63f, 0.49f), new PointF(0.52f, 0.76f)),
            ["de_vertigo"] = (new PointF(0.705f, 0.585f), new PointF(0.222f, 0.223f))
        };

    public static RadarDefinition? Get(string map)
        => Maps.TryGetValue(map ?? "", out var value) ? value : null;

    public static SpawnProfile SpawnProfileFromWorld(
        string map,
        string side,
        float x,
        float y,
        float z)
    {
        var def = Get(map);
        if (def == null)
            return new SpawnProfile(false, "NEUTRAL", "spawn unknown", 0, 0);

        var point = WorldToRadar(def, x, y);

        // Dust2 spawns are laid out almost linearly across T/CT spawn.
        // Bombsite-distance is a poor proxy here: a real B-friendly spawn can
        // look closer to A after overview transforms. Use the actual spawn
        // coordinate bands instead.
        if (string.Equals(map, "de_dust2", StringComparison.OrdinalIgnoreCase))
        {
            var isCt = string.Equals(side, "CT", StringComparison.OrdinalIgnoreCase);

            if (!isCt)
            {
                // Current CS2 T spawn set runs roughly x=-1181 .. -332.
                // Lower x = tunnels/B side, higher x = Long side.
                if (x <= -900)
                    return new SpawnProfile(
                        true, "B", "T spawn • B-fast", point.X, point.Y);

                if (x >= -600)
                    return new SpawnProfile(
                        true, "A", "T spawn • LONG-fast", point.X, point.Y);

                return new SpawnProfile(
                    true, "NEUTRAL", "T spawn • MID/FLEX", point.X, point.Y);
            }

            // CT competitive spawns cluster around x=160 .. 351.
            // Higher x naturally favors A/Long, lower x favors B/mid.
            if (x >= 310)
                return new SpawnProfile(
                    true, "A", "CT spawn • A/LONG-fast", point.X, point.Y);

            if (x <= 190)
                return new SpawnProfile(
                    true, "B", "CT spawn • B-fast", point.X, point.Y);

            return new SpawnProfile(
                true, "NEUTRAL", "CT spawn • MID/FLEX", point.X, point.Y);
        }

        var bias = "NEUTRAL";

        // A/B on Nuke are vertically stacked, so 2D radar distance is not a
        // trustworthy spawn-route signal. Anubis currently has no site markers
        // in our overview metadata. In both cases we preserve the coordinates
        // for AI context but refuse to fabricate a site bias.
        if (!string.Equals(map, "de_nuke", StringComparison.OrdinalIgnoreCase) &&
            BombSites.TryGetValue(map ?? "", out var sites))
        {
            static double Distance(PointF a, PointF b)
            {
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            var a = Distance(point, sites.A);
            var b = Distance(point, sites.B);
            var sum = Math.Max(0.001, a + b);
            var separation = Math.Abs(a - b) / sum;

            if (separation >= 0.06)
                bias = a < b ? "A" : "B";
        }

        var sideLabel = string.IsNullOrWhiteSpace(side)
            ? "spawn"
            : side.Trim().ToUpperInvariant() + " spawn";

        var biasLabel = bias switch
        {
            "A" => "A-leaning",
            "B" => "B-leaning",
            _ => "neutral"
        };

        return new SpawnProfile(
            true,
            bias,
            $"{sideLabel} • {biasLabel} • radar {point.X:0.00},{point.Y:0.00}",
            point.X,
            point.Y);
    }

    public static string BombSiteFromWorld(
        string map,
        float x,
        float y,
        float z)
    {
        if (string.Equals(map, "de_nuke", StringComparison.OrdinalIgnoreCase))
            return z < -495 ? "B" : "A";

        var def = Get(map);
        if (def == null || !BombSites.TryGetValue(map ?? "", out var sites))
            return "";

        var point = WorldToRadar(def, x, y);

        static double Distance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        var aDistance = Distance(point, sites.A);
        var bDistance = Distance(point, sites.B);
        var nearest = Math.Min(aDistance, bDistance);

        // A planted bomb should be close to the official site marker.
        // Refuse to classify uncertain points rather than fabricate a site.
        if (nearest > 0.18)
            return "";

        return aDistance <= bDistance ? "A" : "B";
    }

    public static PointF WorldToRadar(RadarDefinition def, float x, float y)
    {
        double px = (x - def.PosX) / def.Scale;
        double py = (def.PosY - y) / def.Scale;

        if (def.Rotate)
        {
            var oldX = px;
            px = py;
            py = 1024.0 - oldX;
        }

        return new PointF(
            (float)Math.Clamp(px / 1024.0, 0.0, 1.0),
            (float)Math.Clamp(py / 1024.0, 0.0, 1.0));
    }
}
