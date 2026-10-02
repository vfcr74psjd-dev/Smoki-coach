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

        parts.Add(MapRoundPlan(s.Map, s.Team));

        if (d >= Math.Max(3, k + 2))
            parts.Add("Trend: preveč early deaths — naslednjo rundo znižaj prvi risk in ostani tradeable.");
        else if (k >= d + 3 && d > 0)
            parts.Add("Trend: kill output je dober — po prvem killu zaščiti prednost in zamenjaj pozicijo.");

        parts.Add(mode switch
        {
            "Aggressive" => "Mode Aggressive: en kvaliteten opening duel z utilityjem ali tradeom; ne chain-peekaj.",
            "Safe" => "Mode Safe: prioriteta je survival po prvem kontaktu, crossfire in reposition.",
            _ => "Mode Balanced: kontroliran prvi duel, nato trade ali reposition."
        });

        return $"{map}: " + string.Join(" ", parts);
    }

    public static string InstantRoundPlan(
        GameSnapshot s,
        string mode,
        string role,
        string focus,
        IReadOnlyList<RoundRecord> rounds)
    {
        var roundType = ClassifyRound(s);
        var map = PrettyMap(s.Map);
        var mapPlan = MapRoundPlan(s.Map, s.Team);

        var opening = roundType switch
        {
            "Pistol" => "Pistol: drži trade razdaljo in vzemi samo en čist prvi duel.",
            "Eco" => "Eco: izogni se dolgim dry duelom; igraj close/stack za en kill in pobiranje orožja.",
            "Force / light" => "Light buy: izberi kratko razdaljo ali utility-assisted duel; ne razdeli ekipe.",
            _ => "Gun round: utility pred prvim peekanjem, nato en kontroliran opening/trade duel."
        };

        var rolePlan = role switch
        {
            "Entry" => "Entry: commitaj šele, ko je teammate dovolj blizu za trade.",
            "Lurk" => "Lurk: vzemi info/prostor, ampak ne zamudi glavnega kontakta ekipe.",
            "Support" => "Support: odpri prostor z utilityjem in takoj sledi za trade.",
            "Anchor" => "Anchor: prvi kontakt vzemi iz kota z varnim umikom.",
            _ => "Flex: izberi nalogo glede na spawn in ostani tradeable."
        };

        var afterKill = mode switch
        {
            "Aggressive" => "Po killu zamenjaj kot; drugi duel vzemi samo z jasnim timingom ali tradeom.",
            "Safe" => "Po killu se umakni v crossfire in prisili nasprotnika, da pride k tebi.",
            _ => "Po killu reposition; ne repeekaj iste linije brez novega razloga."
        };

        var focusPlan = focus switch
        {
            "Survive & trade" => "Preživi prvi kontakt in ostani v trade razdalji.",
            "Entry impact" => "Ustvari prostor, vendar brez solo chain-peekanja.",
            "Utility impact" => "Vsaj en kos utilityja uporabi pred glavnim duelom.",
            "Clutch / late round" => "Ohrani HP in utility za zadnjih 40 sekund.",
            _ => "Išči en kakovosten kill, nato zaščiti številčno prednost."
        };

        var recent = rounds.TakeLast(3).ToList();
        string trend = "";
        if (recent.Count >= 2)
        {
            var deathRounds = recent.Count(x => x.DeathsRound > 0);
            var zeroRounds = recent.Count(x => x.KillsRound == 0);

            if (deathRounds >= 2 && zeroRounds >= 2)
                trend = " Zadnji trend: znižaj early risk.";
            else if (recent.Count(x => x.KillsRound >= 2) >= 2)
                trend = " Zadnji trend: output je dober — ne podari ga z nepotrebnim repeekom.";
        }

        return
            $"PLAN: {map} {s.Team} • {roundType}. {mapPlan}\n" +
            $"OPENING: {opening} {rolePlan}\n" +
            $"AFTER KILL: {afterKill}\n" +
            $"FOCUS: {focusPlan}{trend}";
    }

    private static string MapRoundPlan(string map, string side)
    {
        bool t = string.Equals(side, "T", StringComparison.OrdinalIgnoreCase);

        return map switch
        {
            "de_mirage" => t
                ? "T plan: vzemi info/control na midu ali rampi; ko dobiš prvi duel, ne ostani na istem kotu in se priključi trade liniji."
                : "CT plan: ne podari mida brez info; po prvem kontaktu igraj connector/jungle/site crossfire in ne repeekaj sam.",

            "de_inferno" => t
                ? "T plan: banana ali apps control z utilityjem, nato počakaj reakcijo; entry naj ima igralca dovolj blizu za trade."
                : "CT plan: zgodaj zadrži banana/apps info z utilityjem, nato se umakni v pozicijo, kjer lahko dobiš kill in preživiš.",

            "de_nuke" => t
                ? "T plan: uporabi lobby/yard pritisk za rotacije; ne teci sam skozi ramp ali door brez trade podpore."
                : "CT plan: čuvaj yard/ramp info in ne lovi killov po prvem kontaktu; Nuke nagrajuje hitro repositionanje med nivoji.",

            "de_ancient" => t
                ? "T plan: mid ali cave control naj odpre prostor za split; po prvem killu takoj zamenjaj kot in ostani ob teammateu."
                : "CT plan: cave/mid info je dragocen; uporabi utility za delay in igraj drugi kontakt iz drugačnega kota.",

            "de_anubis" => t
                ? "T plan: kontrola mida/vode naj pripravi split; ne sili site entryja brez prostora za trade."
                : "CT plan: igraj za info na mid/water in imej pot za umik; po killu se umakni iz pre-aimane linije.",

            "de_dust2" => t
                ? "T plan: long/mid/B info vzemi disciplinirano; izogibaj se solo dry peeku in iz prvega picka naredi številčno prednost."
                : "CT plan: long/mid info vzemi z utilityjem in ne ostajaj izpostavljen; po prvem killu zaščiti crossfire.",

            "de_train" => t
                ? "T plan: najprej ustvari pritisk in info na yard/inner, nato napadi šibkejšo stran; entry mora biti tradeable."
                : "CT plan: uporabi dolge linije za prvi kontakt, vendar po strelu/killu zamenjaj vagon ali globino kota.",

            "de_overpass" => t
                ? "T plan: map control fountain/connector ali short naj pride pred executeom; izogibaj se izoliranim duelom daleč od ekipe."
                : "CT plan: zgodnji info je dober samo, če imaš umik; po kontaktu se vrni v crossfire na site/connector.",

            "de_vertigo" => t
                ? "T plan: ramp/mid prostor vzemi z utilityjem; ne stoj po prvem picku na istem ozkem kotu."
                : "CT plan: delay utility na rampi in kratek prvi kontakt; nato reposition, da te naslednji igralec ne pre-aim-a.",

            _ => t
                ? "T plan: vzemi en kos map controla, ostani tradeable in šele nato commitaj site."
                : "CT plan: prvi kontakt vzemi iz pozicije z umikom; po killu zamenjaj kot in ohrani crossfire."
        };
    }
}
