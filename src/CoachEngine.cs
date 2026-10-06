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

    public static SpawnProfile SpawnProfile(GameSnapshot s)
    {
        if (!s.HasSpawnPosition)
            return new SpawnProfile(false, "NEUTRAL", "spawn not captured", 0, 0);

        return RadarCatalog.SpawnProfileFromWorld(
            s.Map,
            s.Team,
            s.SpawnPositionX!.Value,
            s.SpawnPositionY!.Value,
            s.SpawnPositionZ!.Value);
    }

    public static string SpawnContext(GameSnapshot s)
        => SpawnProfile(s).Label;

    public static string ClassifyRound(GameSnapshot s)
    {
        if (s.IsSpectating) return "Spectating";
        if (s.Round is 0 or 12) return "Pistol";

        var money = s.Money ?? 0;
        var w = (string.IsNullOrWhiteSpace(s.PrimaryWeapon) ? s.Weapon : s.PrimaryWeapon)
            .ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(s.PrimaryWeapon) ||
            w.Contains("ak47") || w.Contains("m4a1") || w.Contains("awp") ||
            w.Contains("aug") || w.Contains("sg556"))
            return "Full buy";

        if (money < 1400) return "Eco";
        if (money < 3200) return "Force / light";
        return "Gun round";
    }

    public static (string title, string advice) BuyAdvice(GameSnapshot s)
    {
        if (s.IsSpectating)
            return (
                "Spectating",
                "SPECTATING • buy se osveži, ko GSI spet vidi tvoj player state.");

        var roundType = ClassifyRound(s);
        var money = s.Money ?? 0;
        var side = s.Team ?? "";

        if (roundType == "Pistol")
        {
            return side == "CT"
                ? ("Pistol", "Balanced: armor za duel ali kit + utility za support. Ne razbij economyja z naključnimi pistol nakupi.")
                : ("Pistol", "Balanced: armor za opening/trade ali utility + P250 samo, če imaš jasen entry plan.");
        }

        if (!string.IsNullOrWhiteSpace(s.PrimaryWeapon))
            return ("Keep " + PrettyWeapon(s.PrimaryWeapon), RoundBuyPlan(s));

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
        IReadOnlyList<RoundRecord> rounds,
        string intent)
    {
        return TacticalBrainV6.Generate(
            s,
            mode,
            role,
            focus,
            rounds,
            intent).RenderLegacyCompatible();
    }

    public static string NormalizeRoundIntent(string? raw)
    {
        var value = (raw ?? "").Trim().ToUpperInvariant();
        return value switch
        {
            "A" => "A",
            "B" => "B",
            _ => ""
        };
    }

    public static string PositionPlan(
        GameSnapshot s,
        string intent,
        IReadOnlyList<RoundRecord> rounds)
    {
        intent = NormalizeRoundIntent(intent);
        var ct = string.Equals(s.Team, "CT", StringComparison.OrdinalIgnoreCase);
        var spawn = SpawnProfile(s);
        var manualIntent = !string.IsNullOrWhiteSpace(intent);

        if (!manualIntent)
            intent = AutoRoundIntent(s, rounds);

        var recentAtIntent = rounds
            .TakeLast(8)
            .Where(r =>
                string.Equals(
                    NormalizeRoundIntent(r.Intent),
                    intent,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var failed = recentAtIntent.Count(r =>
            r.Won == false ||
            r.DeathsRound > 0);

        var lastPosition = recentAtIntent
            .LastOrDefault()?.PositionPlan ?? "";

        var options = ct
            ? CtPositionOptions(s.Map, intent)
            : TPositionOptions(s.Map, intent);

        if (options.Length == 0)
            return ct
                ? $"{intent} site • prvi kontakt + varen umik"
                : $"{intent} route • drugi kontakt + trade";

        int index;

        if (spawn.Known && options.Length > 1)
        {
            // Options are ordered from more direct first-contact to more
            // supportive/passive. Spawn only chooses the variant; it never
            // overrides an explicit A/B call.
            index =
                string.Equals(spawn.Bias, intent, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : spawn.Bias == "NEUTRAL"
                        ? Math.Min(1, options.Length - 1)
                        : options.Length - 1;

            // Repeating a failed/death-heavy setup twice moves one step away
            // from the same look even if the spawn would normally favor it.
            if (failed >= 2)
                index = (index + 1) % options.Length;
        }
        else
        {
            var variant = Math.Abs((s.Round ?? 0) % 3);
            index = Math.Abs(variant + failed) % options.Length;
        }

        if (options.Length > 1 &&
            string.Equals(
                options[index],
                lastPosition,
                StringComparison.OrdinalIgnoreCase))
        {
            index = (index + 1) % options.Length;
        }

        // Dust2 safety guard: if patterns still choose A from a B-side T spawn,
        // never suggest the fast Long option. Use a slower support route instead.
        if (!ct &&
            string.Equals(s.Map, "de_dust2", StringComparison.OrdinalIgnoreCase) &&
            spawn.Bias == "B" &&
            intent == "A" &&
            options.Length > 1)
        {
            index = options.Length - 1;
        }

        return options[index];
    }

    public static string AutoRoundIntent(
        GameSnapshot s,
        IReadOnlyList<RoundRecord> rounds)
    {
        var recent = rounds.TakeLast(8).ToList();
        var ct = string.Equals(s.Team, "CT", StringComparison.OrdinalIgnoreCase);
        var spawn = SpawnProfile(s);

        if (recent.Count == 0)
        {
            if (spawn.Known && spawn.Bias is "A" or "B")
                return spawn.Bias;

            return (s.Round ?? 0) % 2 == 0 ? "A" : "B";
        }

        if (ct)
        {
            var observedPlants = recent
                .Where(r =>
                    r.BombPlanted &&
                    (r.BombSite == "A" || r.BombSite == "B"))
                .ToList();

            if (observedPlants.Count >= 2)
            {
                var aWeight = observedPlants
                    .Select((r, i) => (Round: r, Weight: i + 1))
                    .Where(x => x.Round.BombSite == "A")
                    .Sum(x => x.Weight);
                var bWeight = observedPlants
                    .Select((r, i) => (Round: r, Weight: i + 1))
                    .Where(x => x.Round.BombSite == "B")
                    .Sum(x => x.Weight);

                if (aWeight != bWeight)
                    return aWeight > bWeight ? "A" : "B";
            }
        }

        int ScoreSite(string site)
        {
            var siteRounds = recent
                .Where(r => string.Equals(
                    NormalizeRoundIntent(r.Intent),
                    site,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (siteRounds.Count == 0)
                return 1;

            var losses = siteRounds.Count(r => r.Won == false);
            var deaths = siteRounds.Count(r => r.DeathsRound > 0);
            var wins = siteRounds.Count(r => r.Won == true);
            var survivedWins = siteRounds.Count(r => r.Won == true && r.Survived);

            // CT: reinforce a site that has recently leaked rounds.
            // T: favor the side that has recently produced wins/survival.
            return ct
                ? losses * 4 + deaths * 2 - wins - survivedWins
                : wins * 3 + survivedWins * 2 - losses * 2 - deaths;
        }

        var a = ScoreSite("A");
        var b = ScoreSite("B");

        // Spawn is a route advantage, not an absolute command.
        // On T side it gets meaningful weight; on CT side enemy patterns
        // should have more authority, so the spawn bonus is smaller.
        if (spawn.Known)
        {
            var spawnWeight = ct ? 2 : 4;

            if (spawn.Bias == "A") a += spawnWeight;
            if (spawn.Bias == "B") b += spawnWeight;
        }

        if (a == b)
        {
            if (spawn.Known && spawn.Bias is "A" or "B")
                return spawn.Bias;

            var lastSite = NormalizeRoundIntent(recent.LastOrDefault()?.Intent);

            if (lastSite == "A") return "B";
            if (lastSite == "B") return "A";

            return (s.Round ?? 0) % 2 == 0 ? "A" : "B";
        }

        return a > b ? "A" : "B";
    }

    public static string EnemyExpectation(
        GameSnapshot s,
        IReadOnlyList<RoundRecord> rounds)
    {
        var ct = string.Equals(s.Team, "CT", StringComparison.OrdinalIgnoreCase);

        var recent = rounds
            .Where(r => string.Equals(
                r.Side,
                s.Team,
                StringComparison.OrdinalIgnoreCase))
            .TakeLast(8)
            .ToList();

        if (recent.Count == 0)
            return ct
                ? "no enemy sample yet • hold standard setup • do not overrotate • confidence LOW"
                : "no CT setup sample yet • take one tradeable contact and collect info • confidence LOW";

        if (ct)
        {
            var plants = recent
                .Where(r => r.BombPlanted)
                .ToList();

            var knownSites = plants
                .Where(r => r.BombSite is "A" or "B")
                .ToList();

            var enemyWins = recent.Count(r => r.Won == false);
            var plantRate = (int)Math.Round(
                100.0 * plants.Count / recent.Count);

            var sitePart = "site unknown";
            var confidence = "LOW";

            if (knownSites.Count >= 2)
            {
                var a = knownSites.Count(r => r.BombSite == "A");
                var b = knownSites.Count(r => r.BombSite == "B");

                if (a == b)
                {
                    sitePart = $"A/B split {a}:{b}";
                    confidence = knownSites.Count >= 4 ? "MEDIUM" : "LOW";
                }
                else
                {
                    var site = a > b ? "A" : "B";
                    var dominant = Math.Max(a, b);
                    sitePart = $"{site} pressure {dominant}/{knownSites.Count} known plants";

                    confidence =
                        knownSites.Count >= 4 &&
                        Math.Abs(a - b) >= 2
                            ? "HIGH"
                            : "MEDIUM";
                }

                var lastTwo = knownSites.TakeLast(2).ToList();
                if (lastTwo.Count == 2 &&
                    lastTwo[0].BombSite == lastTwo[1].BombSite)
                {
                    sitePart += $" • repeat {lastTwo[1].BombSite}";
                }
            }
            else if (knownSites.Count == 1)
            {
                sitePart = $"last known plant {knownSites[0].BombSite}";
            }

            var timedPlants = plants
                .Where(r => r.BombPlantSeconds.HasValue)
                .ToList();

            var tempoPart = "";
            if (timedPlants.Count >= 2)
            {
                var fast = timedPlants.Count(r => r.BombPlantSeconds <= 40);
                var late = timedPlants.Count(r => r.BombPlantSeconds >= 70);

                tempoPart =
                    fast >= Math.Max(2, late + 1)
                        ? " • fast tendency"
                        : late >= Math.Max(2, fast + 1)
                            ? " • late tendency"
                            : " • mixed tempo";
            }

            var conversionPart =
                enemyWins >= Math.Ceiling(recent.Count * 0.65)
                    ? " • enemy converting well"
                    : enemyWins <= Math.Floor(recent.Count * 0.35)
                        ? " • enemy conversion low"
                        : "";

            return
                $"{sitePart}{tempoPart}{conversionPart} • " +
                $"plants {plantRate}% • sample {recent.Count}/8 • confidence {confidence}";
        }

        var aRounds = recent
            .Where(r => NormalizeRoundIntent(r.Intent) == "A")
            .ToList();
        var bRounds = recent
            .Where(r => NormalizeRoundIntent(r.Intent) == "B")
            .ToList();

        if (recent.Count == 1)
        {
            var only = recent[0];
            var site = NormalizeRoundIntent(only.Intent);
            if (site is "A" or "B")
            {
                var result = only.Won == true
                    ? "we converted"
                    : only.Won == false
                        ? "CT stopped us"
                        : "result unresolved";

                return $"{site} first sample • {result} • one sample only • confidence LOW";
            }

            return "first CT sample collected • no site pattern yet • confidence LOW";
        }

        static int Stops(List<RoundRecord> siteRounds)
            => siteRounds.Count(r => r.Won == false);

        var aStops = Stops(aRounds);
        var bStops = Stops(bRounds);

        string defensePart;
        string defenseConfidence;

        if (aRounds.Count >= 2 || bRounds.Count >= 2)
        {
            var aRate = aRounds.Count == 0
                ? 0
                : (double)aStops / aRounds.Count;
            var bRate = bRounds.Count == 0
                ? 0
                : (double)bStops / bRounds.Count;

            if (aRounds.Count >= 2 &&
                (bRounds.Count < 2 || aRate > bRate + 0.20))
            {
                defensePart =
                    $"A defense stronger • stopped {aStops}/{aRounds.Count}";
                defenseConfidence = aRounds.Count >= 4 ? "HIGH" : "MEDIUM";
            }
            else if (bRounds.Count >= 2 &&
                     (aRounds.Count < 2 || bRate > aRate + 0.20))
            {
                defensePart =
                    $"B defense stronger • stopped {bStops}/{bRounds.Count}";
                defenseConfidence = bRounds.Count >= 4 ? "HIGH" : "MEDIUM";
            }
            else
            {
                defensePart =
                    $"defense balanced • A {aStops}/{Math.Max(1,aRounds.Count)} stops • B {bStops}/{Math.Max(1,bRounds.Count)} stops";
                defenseConfidence = recent.Count >= 5 ? "MEDIUM" : "LOW";
            }
        }
        else
        {
            defensePart =
                $"learning CT setup • A samples {aRounds.Count} • B samples {bRounds.Count}";
            defenseConfidence = "LOW";
        }

        var enemyCtWins = recent.Count(r => r.Won == false);
        var pressure =
            enemyCtWins >= Math.Ceiling(recent.Count * 0.65)
                ? " • CT defense winning"
                : enemyCtWins <= Math.Floor(recent.Count * 0.35)
                    ? " • CT defense vulnerable"
                    : "";

        return
            $"{defensePart}{pressure} • sample {recent.Count}/8 • confidence {defenseConfidence}";
    }

    public static string[] GetPositionOptions(
        string map,
        string side,
        string intent)
    {
        intent = NormalizeRoundIntent(intent);

        return string.Equals(
                side,
                "CT",
                StringComparison.OrdinalIgnoreCase)
            ? CtPositionOptions(map, intent)
            : TPositionOptions(map, intent);
    }

    private static string[] CtPositionOptions(string map, string intent)
    {
        return (map, intent) switch
        {
            ("de_mirage", "B") => new[] { "B apps • bench/van off-angle", "short • cat info + fall back", "market • door/window crossfire" },
            ("de_mirage", "A") => new[] { "ticket • A ramp line + escape", "jungle • connector trade angle", "triple/default • site crossfire" },

            ("de_inferno", "B") => new[] { "banana • car/coffins delay", "coffins • anti-exec crossfire", "CT • passive B retake angle" },
            ("de_inferno", "A") => new[] { "pit • apps/short crossfire", "site • default/new box trade", "library/long • rotate-ready angle" },

            ("de_dust2", "B") => new[] { "B window • tunnels info", "B doors • crossfire + escape", "back plat • passive anti-rush" },
            ("de_dust2", "A") => new[] { "A long • car/site support", "cat • short info + fall back", "A site • ramp/goose crossfire" },

            ("de_nuke", "B") => new[] { "ramp • first contact + fall lower", "B site • decon crossfire", "control/decon • late anti-plant angle" },
            ("de_nuke", "A") => new[] { "hut • close info + fall", "rafters • A crossfire", "main • outside/A rotate angle" },

            ("de_ancient", "B") => new[] { "cave • first contact + fall", "B site • pillar crossfire", "CT • passive B retake angle" },
            ("de_ancient", "A") => new[] { "donut • mid/A trade", "A site • default crossfire", "temple • passive anti-exec" },

            ("de_anubis", "B") => new[] { "B connector • water trade", "B site • back-site crossfire", "CT • passive B retake angle" },
            ("de_anubis", "A") => new[] { "A main • early info + fall", "A site • default crossfire", "heaven/connector • rotate-ready angle" },

            ("de_overpass", "B") => new[] { "monster • pillar/short crossfire", "short B • info + fall", "B site • barrels/pillar anchor" },
            ("de_overpass", "A") => new[] { "bathrooms • info + fall", "long A • passive line", "A site • truck/bank crossfire" },

            ("de_vertigo", "B") => new[] { "B stairs • first contact + fall", "B site • back-site crossfire", "mid/B rotate • passive support" },
            ("de_vertigo", "A") => new[] { "A ramp • sandbags support", "A site • default crossfire", "elevator/short • rotate-ready angle" },

            ("de_train", "B") => new[] { "B upper • first contact + fall", "B lower • crossfire", "B site • passive anchor" },
            ("de_train", "A") => new[] { "A main • info + fall", "ivy • long angle + escape", "connector • site trade/rotate" },

            (_, "B") => new[] { "B site • first contact + varen umik", "B rotate angle • drugi kontakt" },
            (_, "A") => new[] { "A site • first contact + varen umik", "A rotate angle • drugi kontakt" },
            _ => Array.Empty<string>()
        };
    }

    private static string[] TPositionOptions(string map, string intent)
    {
        return (map, intent) switch
        {
            ("de_mirage", "B") => new[] { "B apps • drugi kontakt za trade", "underpass → short • pozni split", "apps exit • utility support" },
            ("de_mirage", "A") => new[] { "A ramp • drugi kontakt", "palace • pozni trade", "connector • split support" },

            ("de_inferno", "B") => new[] { "banana • drugi kontakt", "logs/car • utility support", "banana late • trade za execute" },
            ("de_inferno", "A") => new[] { "apps • drugi kontakt", "short • trade entry", "long • late split support" },

            ("de_dust2", "B") => new[] { "upper tunnels • drugi kontakt", "B tunnels • flash + trade", "mid → B • split support" },
            ("de_dust2", "A") => new[] { "long • drugi kontakt", "short • cat trade", "A ramp • post-entry support" },

            ("de_nuke", "B") => new[] { "ramp • drugi kontakt", "secret • lower split support", "decon • late trade" },
            ("de_nuke", "A") => new[] { "hut • drugi kontakt", "squeaky • utility trade", "main • A split support" },

            ("de_ancient", "B") => new[] { "B ramp • drugi kontakt", "cave • trade support", "mid → B • split timing" },
            ("de_ancient", "A") => new[] { "A main • drugi kontakt", "donut • split support", "A main late • utility trade" },

            ("de_anubis", "B") => new[] { "B main • drugi kontakt", "water • split support", "connector • late trade" },
            ("de_anubis", "A") => new[] { "A main • drugi kontakt", "mid → A • split support", "A main late • utility trade" },

            ("de_overpass", "B") => new[] { "monster • drugi kontakt", "short B • split support", "water • late trade" },
            ("de_overpass", "A") => new[] { "bathrooms • drugi kontakt", "long A • trade support", "connector → A • late split" },

            ("de_vertigo", "B") => new[] { "B stairs • drugi kontakt", "mid → B • split support", "B execute • utility trade" },
            ("de_vertigo", "A") => new[] { "A ramp • drugi kontakt", "short • split support", "ramp late • utility trade" },

            ("de_train", "B") => new[] { "B upper • drugi kontakt", "B lower • trade support", "connector route • late split" },
            ("de_train", "A") => new[] { "A main • drugi kontakt", "ivy • split support", "connector • late trade" },

            (_, "B") => new[] { "B route • drugi kontakt + trade" },
            (_, "A") => new[] { "A route • drugi kontakt + trade" },
            _ => Array.Empty<string>()
        };
    }

    public static string RoundBuyPlan(GameSnapshot s)
    {
        if (s.IsSpectating)
            return "SPECTATING • buy se osveži ob tvojem naslednjem player state-u.";

        var money = s.Money ?? 0;
        var ct = string.Equals(s.Team, "CT", StringComparison.OrdinalIgnoreCase);
        var pistol = s.Round is 0 or 12;

        if (pistol)
            return ct
                ? (money >= 800 ? "kevlar • ali kit + flash, če igraš support" : "brez force-a")
                : (money >= 800 ? "kevlar • ali smoke + flash + P250 za utility plan" : "brez force-a");

        if (!string.IsNullOrWhiteSpace(s.PrimaryWeapon))
        {
            var gun = PrettyWeapon(s.PrimaryWeapon);
            var armor = s.Armor ?? 0;
            var helmet = s.Helmet == true;

            if (armor < 40)
                return $"KEEP {gun} • kupi armor • nato smoke/flash";

            if (ct)
                return helmet
                    ? $"KEEP {gun} • smoke + flash/kit • brez novega primaryja"
                    : $"KEEP {gun} • helmet po potrebi + smoke/kit";

            return helmet
                ? $"KEEP {gun} • smoke + molly/flash • brez novega primaryja"
                : $"KEEP {gun} • helmet + utility";
        }

        if (money < 1500)
            return "eco • P250 samo, če ne pokvari naslednjega full buyja";

        if (money < 3000)
            return ct
                ? "MP9/FAMAS + kevlar samo ob team force-u • sicer save"
                : "MAC-10/Galil + kevlar samo ob team force-u • sicer save";

        if (ct)
        {
            if (money >= 4200)
                return "M4 + kevlar/helmet + smoke • nato flash/kit";
            return "FAMAS/MP9 + kevlar + smoke • ne uniči naslednjega buyja";
        }

        if (money >= 4100)
            return "AK + kevlar/helmet + smoke + molly";
        return "Galil + kevlar + smoke • ostanek za flash";
    }

    private static string CompactActionPlan(
        GameSnapshot s,
        string role,
        string focus,
        int variant,
        IReadOnlyList<RoundRecord> rounds)
    {
        var mapStep = CompactMapStep(s.Map, s.Team, variant);
        var last = rounds.LastOrDefault();
        var protectedCarry =
            last?.Won == true &&
            last.Survived &&
            !string.IsNullOrWhiteSpace(s.PrimaryWeapon);

        var roleStep = protectedCarry
            ? $"protect {PrettyWeapon(s.PrimaryWeapon)} • igraj tradeable drugi kontakt"
            : role switch
        {
            "Entry" => "entry samo s tradeom",
            "Lurk" => "vzemi info, nato pravočasno joinaj",
            "Support" => "utility pred kontaktom, nato takoj za trade",
            "Anchor" => "prvi kontakt + varen umik",
            _ => "ostani tradeable"
        };

        var focusStep = focus switch
        {
            "Survive & trade" => "po prvem kontaktu ne repeekaj",
            "Entry impact" => "ustvari prostor, brez chain-peeka",
            "Utility impact" => "1 uporaben nade pred duelom",
            "Clutch / late round" => "prihrani HP + utility za late",
            _ => "po killu zamenjaj kot"
        };

        return $"{mapStep} → {roleStep} → {focusStep}";
    }

    private static string CompactAdaptPlan(
        IReadOnlyList<RoundRecord> rounds,
        string mode)
    {
        var last = rounds.LastOrDefault();
        if (last == null)
            return mode == "Aggressive"
                ? "1 kontroliran opening duel • brez drugega dry peeka"
                : "prvi duel naj bo tradeable • oceni kontakt";

        if (last.Won == true && last.Survived && !string.IsNullOrWhiteSpace(last.WeaponEnd))
            return $"W + survived {PrettyWeapon(last.WeaponEnd)} → ohrani gun • manj hero riska";

        if (last.Won == false && last.Survived && !string.IsNullOrWhiteSpace(last.WeaponEnd))
            return $"L + saved {PrettyWeapon(last.WeaponEnd)} → igraj okoli saved guna • tradeable";

        if (last.Won == true && !last.Survived)
            return "prejšnja W, ampak si umrl → izkoristi win economy • brez nepotrebnega openerja";

        if (last.Won == false && !last.Survived)
            return "prejšnja L + death → spremeni opening timing • manj early riska";

        if (last.KillsRound == 0 && last.DeathsRound > 0)
            return "prejšnja 0K/1D → manj early riska • igraj drugi kontakt";

        if (last.KillsRound >= 2 && last.DeathsRound == 0)
            return $"prejšnja {last.KillsRound}K/0D → ohrani tempo • spremeni opening timing";

        if (last.KillsRound >= 2)
            return $"prejšnja {last.KillsRound}K/{last.DeathsRound}D → impact dober • po killu hitreje ven";

        if (last.KillsRound == 0 && last.DeathsRound == 0)
            return "prejšnja 0K/0D → bodi bližje prvemu tradeu";

        return $"prejšnja {last.KillsRound}K/{last.DeathsRound}D → isti plan • drugačen timing";
    }

    public static string PrettyWeapon(string weapon)
    {
        var w = (weapon ?? "").Trim().ToLowerInvariant();
        return w switch
        {
            "ak47" => "AK-47",
            "m4a1" => "M4A4",
            "m4a1_silencer" => "M4A1-S",
            "awp" => "AWP",
            "ssg08" => "SSG 08",
            "galilar" => "Galil",
            "famas" => "FAMAS",
            "sg556" => "SG 553",
            "aug" => "AUG",
            "mp9" => "MP9",
            "mac10" => "MAC-10",
            "ump45" => "UMP-45",
            "mp5sd" => "MP5-SD",
            "mp7" => "MP7",
            "p90" => "P90",
            "bizon" => "PP-Bizon",
            "xm1014" => "XM1014",
            "mag7" => "MAG-7",
            "sawedoff" => "Sawed-Off",
            "nova" => "Nova",
            _ => string.IsNullOrWhiteSpace(weapon) ? "gun" : weapon.ToUpperInvariant()
        };
    }

    private static string CompactMapStep(string map, string side, int variant)
    {
        var t = string.Equals(side, "T", StringComparison.OrdinalIgnoreCase);

        return map switch
        {
            "de_mirage" when t => variant == 0 ? "mid control" : variant == 1 ? "ramp/apps info" : "pozni mid split",
            "de_mirage" => variant == 0 ? "mid info + connector trade" : variant == 1 ? "site crossfire" : "delay utility + reposition",

            "de_inferno" when t => variant == 0 ? "banana control" : variant == 1 ? "apps/mid info" : "pozni execute",
            "de_inferno" => variant == 0 ? "banana/apps info" : variant == 1 ? "pasiven crossfire" : "delay utility",

            "de_nuke" when t => variant == 0 ? "lobby/yard info" : variant == 1 ? "ramp kontakt" : "spremeni nivo",
            "de_nuke" => variant == 0 ? "yard/ramp info" : variant == 1 ? "site setup" : "kontakt + change level",

            "de_ancient" when t => variant == 0 ? "mid/cave control" : variant == 1 ? "info + join main" : "pozni split",
            "de_ancient" => variant == 0 ? "cave/mid info" : variant == 1 ? "site crossfire" : "delay utility",

            "de_anubis" when t => variant == 0 ? "mid/water control" : variant == 1 ? "info brez commita" : "pozni split",
            "de_anubis" => variant == 0 ? "mid/water info" : variant == 1 ? "pasiven drugi kontakt" : "delay utility",

            "de_dust2" when t => variant == 0 ? "long/mid info" : variant == 1 ? "B prostor + trade" : "spremeni tempo",
            "de_dust2" => variant == 0 ? "long/mid info" : variant == 1 ? "site crossfire" : "druga opening linija",

            "de_train" when t => variant == 0 ? "yard/inner info" : variant == 1 ? "trade entry" : "napadi šibkejšo stran",
            "de_train" => variant == 0 ? "dolga linija + reposition" : variant == 1 ? "pasiven crossfire" : "drugačen peek",

            "de_overpass" when t => variant == 0 ? "fountain/connector control" : variant == 1 ? "short info" : "pozni map control",
            "de_overpass" => variant == 0 ? "info + varen umik" : variant == 1 ? "site crossfire" : "connector/short timing",

            "de_vertigo" when t => variant == 0 ? "ramp/mid prostor" : variant == 1 ? "info brez dolgega stanja" : "pozni commit",
            "de_vertigo" => variant == 0 ? "ramp delay" : variant == 1 ? "pasiven crossfire" : "drugačen timing",

            _ => t ? "1 kos map controla" : "prvi kontakt z umikom"
        };
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
