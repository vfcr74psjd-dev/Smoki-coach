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
        var displayRound = (s.Round ?? 0) + 1;
        var variant = Math.Abs((s.Round ?? 0) % 3);

        var scoreContext = ScoreContext(s);
        var mapPlan = MapRoundPlanVariant(s.Map, s.Team, variant);
        var recentAdjustment = RecentRoundAdjustment(rounds);
        var opening = OpeningPlan(roundType, variant);

        var rolePlan = role switch
        {
            "Entry" => "Entry: commitaj šele, ko je teammate dovolj blizu za trade.",
            "Lurk" => "Lurk: vzemi info/prostor, potem se pravočasno priključi glavnemu kontaktu.",
            "Support" => "Support: odpri prostor z utilityjem in takoj sledi za trade.",
            "Anchor" => "Anchor: prvi kontakt vzemi iz kota z varnim umikom.",
            _ => variant switch
            {
                0 => "Flex: igraj najmočnejši spawn, ampak ostani tradeable.",
                1 => "Flex: začni za info, nato se priključi strani, kjer ima ekipa kontakt.",
                _ => "Flex: ne sili prvega duela; bodi drugi kontakt in pobiraj trade."
            }
        };

        var afterKill = mode switch
        {
            "Aggressive" => variant == 1
                ? "Po prvem killu vzemi prostor samo, če imaš teammate trade; sicer zamenjaj kot."
                : "Po killu takoj spremeni kot; drugega duela ne jemlji iz iste linije.",
            "Safe" => variant == 2
                ? "Po killu zadrži številčno prednost in prisili nasprotnika v retake/entry."
                : "Po killu se umakni v crossfire in ohrani HP.",
            _ => variant switch
            {
                0 => "Po killu reposition; ne repeekaj iste linije brez novega razloga.",
                1 => "Po killu za 2–3 sekunde prekini kontakt, nato pomagaj najbližjemu teammateu.",
                _ => "Po killu zaščiti trade linijo in pusti nasprotniku, da naredi naslednjo napako."
            }
        };

        var focusPlan = focus switch
        {
            "Survive & trade" => "Prioriteta: preživi prvi kontakt in ostani v trade razdalji.",
            "Entry impact" => "Prioriteta: ustvari prostor brez solo chain-peekanja.",
            "Utility impact" => "Prioriteta: vsaj en uporaben utility pred glavnim duelom.",
            "Clutch / late round" => "Prioriteta: ohrani HP in utility za zadnjih 40 sekund.",
            _ => "Prioriteta: en kakovosten kill, potem zaščiti številčno prednost."
        };

        return
            $"PLAN R{displayRound}: {map} {s.Team} • {roundType} • {scoreContext}. {mapPlan}\n" +
            $"OPENING: {opening} {rolePlan}\n" +
            $"ADAPT: {recentAdjustment}\n" +
            $"AFTER KILL: {afterKill} {focusPlan}";
    }

    private static string ScoreContext(GameSnapshot s)
    {
        var ct = s.CtScore ?? 0;
        var t = s.TScore ?? 0;
        var isCt = string.Equals(s.Team, "CT", StringComparison.OrdinalIgnoreCase);
        var own = isCt ? ct : t;
        var enemy = isCt ? t : ct;
        var diff = own - enemy;

        if (diff >= 3)
            return $"vodiš {own}:{enemy} — ne podari openinga";
        if (diff <= -3)
            return $"zaostajaš {own}:{enemy} — vzemi kontrolirano iniciativo";
        if (diff == 0)
            return $"izenačeno {own}:{enemy} — disciplina pred riskom";

        return diff > 0
            ? $"tesno vodiš {own}:{enemy}"
            : $"tesno zaostajaš {own}:{enemy}";
    }

    private static string OpeningPlan(string roundType, int variant)
    {
        if (roundType == "Pistol")
            return variant switch
            {
                0 => "Pistol: drži trade razdaljo in vzemi samo en čist prvi duel.",
                1 => "Pistol: ne loči se od ekipe; prvi kontakt naj bo takoj tradeable.",
                _ => "Pistol: igraj za info in crossfire, ne za hero peek."
            };

        if (roundType == "Eco")
            return variant switch
            {
                0 => "Eco: igraj close/stack za en kill in možnost pobiranja orožja.",
                1 => "Eco: izogni se dolgim dry duelom; išči dvojni peek ali crossfire.",
                _ => "Eco: ne razprši ekipe; skoncentriraj možnost za upgrade orožja."
            };

        if (roundType == "Force / light")
            return variant switch
            {
                0 => "Light buy: išči krajšo razdaljo in utility-assisted prvi duel.",
                1 => "Light buy: ne tekmuj z rifle-i na njihovih idealnih linijah; spremeni razdaljo.",
                _ => "Light buy: igraj za trade in pobiranje boljšega orožja, ne za solo opening."
            };

        return variant switch
        {
            0 => "Gun round: utility pred prvim peekanjem, nato en kontroliran opening/trade duel.",
            1 => "Gun round: začni za info; prvi resen duel vzemi šele, ko imaš trade ali utility.",
            _ => "Gun round: ne odpri z istim timingom kot prejšnjo rundo; najprej spremeni tempo."
        };
    }

    private static string RecentRoundAdjustment(IReadOnlyList<RoundRecord> rounds)
    {
        var last = rounds.LastOrDefault();
        if (last == null)
            return "Ni še dovolj podatkov — začni disciplinirano in oceni prvi kontakt.";

        if (last.DeathsRound > 0 && last.KillsRound == 0)
            return $"Prejšnja runda: 0K/{last.DeathsRound}D. Znižaj early risk in naj bo prvi duel tradeable.";

        if (last.DeathsRound > 0 && last.KillsRound >= 2)
            return $"Prejšnja runda: {last.KillsRound}K/{last.DeathsRound}D. Impact je bil dober, a ne ponovi istega izpostavljenega zaključka.";

        if (last.DeathsRound == 0 && last.KillsRound >= 2)
            return $"Prejšnja runda: {last.KillsRound}K in preživel. Ohraniva formo, ampak spremeni opening kot/timing.";

        if (last.DeathsRound == 0 && last.KillsRound == 0)
            return "Prejšnjo rundo si preživel brez killa. Bodi malo bližje prvemu trade kontaktu.";

        return $"Prejšnja runda: {last.KillsRound}K/{last.DeathsRound}D. Ohraniva strukturo, spremeni pa prvi timing.";
    }

    private static string MapRoundPlanVariant(string map, string side, int variant)
    {
        bool t = string.Equals(side, "T", StringComparison.OrdinalIgnoreCase);

        return map switch
        {
            "de_mirage" when !t => variant switch
            {
                0 => "Začni z mid info/supportom; če ni kontakta, hitro se vrni v site crossfire.",
                1 => "Ne podari openinga na midu; pripravi connector/jungle trade in reagiraj na prvi info.",
                _ => "Spremeni začetni timing: manj early repeeka, več utilityja za delay in varen drugi kontakt."
            },
            "de_mirage" => variant switch
            {
                0 => "Vzemi mid info/control, nato se priključi strani z najboljšim tradeom.",
                1 => "Začni ramp/apps za info; brez picka ne sili site entryja.",
                _ => "Spremeni tempo: utility za space, nato pozni mid ali split namesto istega dry openinga."
            },

            "de_inferno" when !t => variant switch
            {
                0 => "Banana/apps info vzemi z utilityjem in ohrani varen umik.",
                1 => "Igraj bolj pasiven prvi kontakt in pripravi drugi kot za trade/crossfire.",
                _ => "Ne ponovi istega early peeka; delay utility in rotacija naj ustvarita naslednji duel."
            },
            "de_inferno" => variant switch
            {
                0 => "Banana control z utilityjem, potem počakaj reakcijo.",
                1 => "Apps/mid info naj odpre možnost za split; entry naj bo tradeable.",
                _ => "Spremeni tempo in zadrži utility za kasnejši site commit."
            },

            "de_nuke" when !t => variant switch
            {
                0 => "Čuvaj yard/ramp info in imej jasen umik po prvem kontaktu.",
                1 => "Začni bolj site-oriented; ne lovi killov skozi yard brez podpore.",
                _ => "Spremeni nivo po prvem kontaktu in prisili nasprotnika, da ponovno išče tvoj kot."
            },
            "de_nuke" => variant switch
            {
                0 => "Lobby/yard pritisk naj najprej pridobi info o rotacijah.",
                1 => "Ramp/door kontakt vzemi samo s trade podporo.",
                _ => "Spremeni nivo in tempo; ne commitaj prvega sitea brez info."
            },

            "de_ancient" when !t => variant switch
            {
                0 => "Cave/mid info z utilityjem, nato varen drugi kontakt.",
                1 => "Ne overfightaj mida; pripravi site crossfire in rotacijo na prvi info.",
                _ => "Spremeni opening kot in uporabi delay utility pred ponovnim kontaktom."
            },
            "de_ancient" => variant switch
            {
                0 => "Mid/cave control naj odpre split in trade linije.",
                1 => "Vzemi info na drugi strani mape, potem se priključi glavnemu kontaktu.",
                _ => "Spremeni tempo; brez prostora ne sili site entryja."
            },

            "de_anubis" when !t => variant switch
            {
                0 => "Igraj za varen mid/water info in ohrani pot za umik.",
                1 => "Začni bolj pasivno in pripravi drugi kontakt iz crossfirea.",
                _ => "Delay utility naj zamenja dry early duel; nato reposition."
            },
            "de_anubis" => variant switch
            {
                0 => "Mid/water control naj pripravi split.",
                1 => "Začni za info brez commit-a; entry šele s trade prostorom.",
                _ => "Spremeni tempo in zadrži utility za poznejši execute."
            },

            "de_dust2" when !t => variant switch
            {
                0 => "Long/mid info vzemi z utilityjem in ne ostajaj izpostavljen.",
                1 => "Začni z varnejšim site setupom in reagiraj na prvi info.",
                _ => "Spremeni opening linijo; po kontaktu takoj zaščiti crossfire."
            },
            "de_dust2" => variant switch
            {
                0 => "Long/mid info vzemi disciplinirano in brez solo dry peeka.",
                1 => "B info naj bo samo za prostor; glavni duel vzemi s tradeom.",
                _ => "Spremeni tempo in iz prvega picka naredi številčno prednost."
            },

            "de_train" when !t => variant switch
            {
                0 => "Prvi kontakt vzemi z dolgo linijo in takoj spremeni vagon/globino.",
                1 => "Začni bolj passivno; drugi duel naj pride iz crossfirea.",
                _ => "Ne ponavljaj istega peeka med vagoni; utility naj ustvari nov timing."
            },
            "de_train" => variant switch
            {
                0 => "Yard/inner pritisk naj najprej pridobi info.",
                1 => "Entry commit šele, ko je trade linija pripravljena.",
                _ => "Spremeni tempo in napadi šibkejšo stran po prvi reakciji."
            },

            "de_overpass" when !t => variant switch
            {
                0 => "Zgodnji info vzemi samo z jasno potjo za umik.",
                1 => "Začni bližje site crossfireu in prisili T-je v prvi commit.",
                _ => "Spremeni timing na connector/short in ne ostajaj po prvem kontaktu."
            },
            "de_overpass" => variant switch
            {
                0 => "Fountain/connector control naj pride pred executeom.",
                1 => "Short info vzemi s teammateom in ostani tradeable.",
                _ => "Spremeni tempo; poznejši map control naj odpre boljši site commit."
            },

            "de_vertigo" when !t => variant switch
            {
                0 => "Delay utility na rampi, nato kratek prvi kontakt in reposition.",
                1 => "Začni bolj pasivno in pripravi crossfire namesto drugega early peeka.",
                _ => "Spremeni timing na ramp/mid in ne ostajaj v pre-aimani liniji."
            },
            "de_vertigo" => variant switch
            {
                0 => "Ramp/mid prostor vzemi z utilityjem in trade podporo.",
                1 => "Začni za info brez dolgega stanja na istem ozkem kotu.",
                _ => "Spremeni tempo in commitaj šele, ko je odprt trade prostor."
            },

            _ when t => variant switch
            {
                0 => "Vzemi en kos map controla in ostani tradeable.",
                1 => "Začni za info brez zgodnjega commita; reagiraj na prvi kontakt ekipe.",
                _ => "Spremeni tempo in prvi pravi duel vzemi s podporo."
            },
            _ => variant switch
            {
                0 => "Prvi kontakt vzemi iz pozicije z umikom.",
                1 => "Začni bolj pasivno in pripravi drugi kontakt iz crossfirea.",
                _ => "Spremeni opening timing in ne ponavljaj iste linije."
            }
        };
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
