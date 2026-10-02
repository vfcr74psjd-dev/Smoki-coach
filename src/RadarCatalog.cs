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

    public static RadarDefinition? Get(string map)
        => Maps.TryGetValue(map ?? "", out var value) ? value : null;

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
