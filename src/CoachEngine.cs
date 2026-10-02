namespace Sm0kiSoloCoach;

public static class CoachEngine
{
    private static readonly Dictionary<string, string> MapNames = new()
    {
        ["de_mirage"] = "Mirage",
        ["de_inferno"] = "Inferno",
        ["de_nuke"] = "Nuke",
        ["de_ancient"] = "Ancient",
        ["de_anubis"] = "Anubis",
        ["de_dust2"] = "Dust2",
        ["de_train"] = "Train",
        ["de_overpass"] = "Overpass",
        ["de_vertigo"] = "Vertigo",
    };

    public static string PrettyMap(string map)
        => MapNames.TryGetValue(map ?? "", out var n) ? n : (string.IsNullOrWhiteSpace(map) ? "—" : map);

    public static string ClassifyRound(GameSnapshot s)
    {
        if (s.Round is 0 or 12) return "Pistol";

        var money = s.Money ?? 0;
        var w = (s.Weapon ?? "").ToLowerInvariant();

        if (w.Contains("ak47") || w.Contains("m4a1") || w.Contains("awp") ||
            w.Contains("aug") || w.Contains("sg556"))
            return "Full buy";

        if (money < 1400) return "Eco";
        if (money < 3200) return "Force / light";
        return "Gun round";
    }

    public static (string title, string advice) BuyAdvice(GameSnapshot s)
    {
        var roundType = ClassifyRound(s);
        var money = s.Money ?? 0;
        var side = s.Team ?? "";

        if (roundType == "Pistol")
        {
            return side == "CT"
                ? ("Pistol", "Balanced: armor za duel ali kit + utility za support. Ne razbij economyja z naključnimi pistol nakupi.")
                : ("Pistol", "Balanced: armor za opening/trade ali utility + P250 samo, če imaš jasen entry plan.");
        }

        if (money >= 4000 || roundType == "Full buy")
        {
            return side == "CT"
                ? ("Full buy", "M4 + armor + smoke + kit, nato flashes. Če je denarja manj, prioritiziraj rifle + armor.")
                : ("Full buy", "AK-47 + armor + smoke + molotov + 1–2 flashes. Za AVG kills prioritiziraj rifle + armor.");
        }

        if (money < 1600)
            return ("Eco", "Ne sili slabega buyja. P250/Deagle samo, če še vedno ohraniš dober naslednji buy. Igraj close-range za en kill + pobiranje orožja.");

        if (money < 3000)
        {
            return side == "CT"
                ? ("Force / light", "MP9/FAMAS + armor, če team forca. Če ne: save za M4 + utility.")
                : ("Force / light", "MAC-10/Galil + armor, če team forca. Če ne: prihrani za AK.");
        }

        return (roundType, "Balanced buy: armor + najboljši smiseln weapon, nato utility. Ne žrtvuj naslednjega gun rounda za marginalen upgrade.");
    }

    public static string SoloTip(GameSnapshot s, string mode)
    {
        var map = PrettyMap(s.Map);
        var k = s.Kills ?? 0;
        var d = s.Deaths ?? 0;
        var parts = new List<string>();

        if (s.Team == "T")
            parts.Add($"{map} T: išči tradeable duel in ne dry-peekaj brez plana.");
        else if (s.Team == "CT")
            parts.Add($"{map} CT: igraj pozicijo za prvi kill + reposition.");
        else
            parts.Add("Čakam mapo in side za specifičen nasvet.");

        if (d >= Math.Max(3, k + 2))
            parts.Add("Umiraš hitreje kot nabiraš kills — naslednjo rundo znižaj early risk.");
        else if (k >= d + 3 && d > 0)
            parts.Add("Kill output je dober — ne sili hero duela.");

        parts.Add(mode switch
        {
            "Aggressive" => "Aggressive: izberi en kvaliteten opening duel, ne več zapored.",
            "Safe" => "Safe: survival po prvem killu in reposition.",
            _ => "Balanced: en kontroliran duel, nato trade/reposition."
        });

        return string.Join(" ", parts);
    }
}
