using System.IO.Compression;
using Sm0kiSoloCoach;
using System.Text.Json;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            TestOrderedRouteSupport();
            TestWrongSiteContradiction();
            TestFallbackBridge();
            TestOpponentDna();
            TestDecisionMode();
            TestWeaknessMap();
            TestWeaknessPlanPenalty();
            await TestPhoneDashboardAsync();

            if (args.Length != 1 ||
                string.IsNullOrWhiteSpace(args[0]))
            {
                throw new InvalidOperationException(
                    "Expected one CS2 .dem fixture path.");
            }

            await TestRealDemoFormatsAsync(args[0]);

            Console.WriteLine(
                "SM0KI V7 SMOKE PASS • graph + raw/gzip/zip real demo parser");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "SM0KI V7 SMOKE FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void TestOrderedRouteSupport()
    {
        var path =
            new[]
            {
                Sample("T Spawn", "OTHER"),
                Sample("Mid", "MID"),
                Sample("Connector", "MID"),
                Sample("A Site", "A")
            };

        var result =
            MapKnowledgeGraphV7.AssessRoute(
                "de_mirage",
                "T Spawn → Mid → Connector → A Site",
                path);

        Require(
            result.Evidence == "SUPPORTED",
            "Mirage ordered route should be SUPPORTED.");

        Require(
            result.AdherenceScore >= 0.85,
            "Mirage ordered route adherence should be high.");

        Require(
            result.DestinationReached,
            "Mirage route destination should be reached.");
    }

    private static void TestWrongSiteContradiction()
    {
        var path =
            new[]
            {
                Sample("A Ramp", "A"),
                Sample("Palace", "A"),
                Sample("A Site", "A")
            };

        var result =
            MapKnowledgeGraphV7.AssessRoute(
                "de_mirage",
                "B Apps → B Site",
                path);

        Require(
            result.Evidence == "CONTRADICTED",
            "A-site execution must contradict a B-site plan.");
    }

    private static void TestFallbackBridge()
    {
        var bridge =
            MapKnowledgeGraphV7.BuildFallbackBridge(
                "de_mirage",
                "Mid → Connector → A Site",
                "B Apps → B Site");

        Require(
            !string.IsNullOrWhiteSpace(bridge),
            "Map graph should produce a fallback bridge.");
    }

    private static void TestOpponentDna()
    {
        var scout =
            new OpponentScoutReport
            {
                Map = "de_mirage",
                TeamMapWinRate = 63,
                TeamRecentKd = 1.16,
                BiggestThreat = "enemy_star",
                Confidence = "HIGH"
            };

        var demo =
            new OpponentDemoIntelReport
            {
                Map = "de_mirage",
                OpeningSamples = 14,
                TopTOpeningZone = "A",
                TopCtOpeningZone = "MID",
                TOpeningKills = 7,
                TOpeningDeaths = 3,
                CtOpeningKills = 6,
                CtOpeningDeaths = 4,
                Confidence = "HIGH",
                TContactZones =
                    new Dictionary<string,int>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["A"] = 8,
                        ["B"] = 2,
                        ["MID"] = 2
                    },
                CtContactZones =
                    new Dictionary<string,int>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["MID"] = 7,
                        ["A"] = 2,
                        ["B"] = 2
                    },
                OpeningKillsByPlayer =
                    new Dictionary<string,int>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["enemy_star"] = 5
                    }
            };

        var dna =
            OpponentDnaEngine.Build(
                "de_mirage",
                scout,
                demo);

        Require(
            dna.Confidence == "HIGH",
            "Opponent DNA should be high confidence.");

        Require(
            dna.TPressureZone == "A" &&
            dna.TPressureShare > 0.60,
            "Opponent DNA should detect T-side A pressure.");

        var ctA =
            OpponentDnaEngine.ScorePlan(
                dna,
                "CT",
                "A",
                "ticket • A ramp line + escape");

        var ctB =
            OpponentDnaEngine.ScorePlan(
                dna,
                "CT",
                "B",
                "B apps • bench/van off-angle");

        Require(
            ctA > ctB,
            "CT predictive model should reinforce the observed T pressure site.");

        var tMid =
            OpponentDnaEngine.ScorePlan(
                dna,
                "T",
                "A",
                "Mid → Connector → A Site");

        Require(
            tMid < 50,
            "T predictive model should de-weight a strong CT opening-control zone.");
    }

    private static void TestDecisionMode()
    {
        var low =
            new TacticalPlanV6(
                "T",
                "A",
                "buy",
                "route",
                "first",
                "expect",
                "fallback",
                "focus",
                "why",
                "LOW",
                55)
            {
                SimulationConfidence = "LOW",
                SimulationMargin = 2
            };

        Require(
            low.DecisionMode == "LOW EVIDENCE",
            "Low simulation confidence must be visible to the phone.");

        var strong =
            low with
            {
                SimulationConfidence = "HIGH",
                SimulationMargin = 8
            };

        Require(
            strong.DecisionMode == "PREDICTIVE",
            "High confidence with a clear margin should be predictive.");
    }

    private static void TestWeaknessMap()
    {
        var routes =
            new List<DigitalTwinRouteNode>
            {
                new()
                {
                    Map = "de_mirage",
                    Side = "CT",
                    Route = "ticket • A ramp line + escape",
                    Attempts = 8,
                    Wins = 2,
                    Survived = 2,
                    ZeroImpactDeaths = 4,
                    OutcomeScoreSum = 2.8
                },
                new()
                {
                    Map = "de_mirage",
                    Side = "T",
                    Route = "B Apps → B Site",
                    Attempts = 8,
                    Wins = 6,
                    Survived = 5,
                    ZeroImpactDeaths = 1,
                    OutcomeScoreSum = 5.8
                }
            };

        var report =
            new GroundTruthMatchReport
            {
                Map = "de_mirage",
                Rounds =
                    new List<GroundTruthRoundVerdict>
                    {
                        new()
                        {
                            Side = "CT",
                            RecommendedRoute = "ticket • A ramp line + escape",
                            GroundTruthEligible = true,
                            Diagnosis = "OPENING_DUEL_LOSS"
                        },
                        new()
                        {
                            Side = "CT",
                            RecommendedRoute = "ticket • A ramp line + escape",
                            GroundTruthEligible = true,
                            Diagnosis = "OPENING_DUEL_LOSS"
                        }
                    }
            };

        var weakness =
            PersonalWeaknessMapV8.Build(
                "de_mirage",
                routes,
                new[] { report });

        Require(
            weakness.Side == "CT",
            "Weakness map should identify the weaker CT route.");

        Require(
            weakness.Route.Contains(
                "ticket",
                StringComparison.OrdinalIgnoreCase),
            "Weakness map should surface the weak route.");

        Require(
            weakness.Leak == "OPENING DUEL",
            "Verified Ground Truth diagnosis should drive the leak label.");
    }

    private static void TestWeaknessPlanPenalty()
    {
        var weakness =
            new PersonalWeaknessMapV8Report(
                "de_mirage",
                "CT",
                "ticket • A ramp line + escape",
                "OPENING DUEL",
                "8 route samples • 3 verified opening duel events",
                "Use a safer second-contact hold.",
                11,
                "HIGH");

        var weakDirect =
            PersonalWeaknessMapV8.ScorePlan(
                weakness,
                "CT",
                "ticket • A ramp line + escape",
                0,
                3);

        var saferAlt =
            PersonalWeaknessMapV8.ScorePlan(
                weakness,
                "CT",
                "jungle • connector trade angle",
                2,
                3);

        Require(
            saferAlt > weakDirect,
            "Predictive planner should de-weight a verified personal weakness route.");
    }

    private static async Task TestPhoneDashboardAsync()
    {
        var snapshot =
            new GameSnapshot
            {
                PlayerName = "Sm0ki",
                Team = "CT",
                Map = "de_mirage",
                MapPhase = "live",
                Round = 4,
                RoundPhase = "freezetime",
                CtScore = 2,
                TScore = 2,
                Health = 100,
                Armor = 100,
                Helmet = true,
                Money = 4800,
                Kills = 5,
                Deaths = 3,
                Assists = 1,
                Weapon = "m4a1_silencer",
                PrimaryWeapon = "m4a1_silencer",
                Timestamp = DateTime.UtcNow
            };

        using var server =
            new PhoneDashboardServer(
                () => snapshot,
                () => "Balanced",
                () => "Flex",
                () => "More kills",
                () => "Sm0ki",
                () => false,
                () => "",
                () => Array.Empty<RoundRecord>(),
                _ => "",
                (_, _, _, _) => { });

        server.Start();

        var publicUrl =
            new Uri(
                server.GetLocalUrl());

        var localPage =
            new UriBuilder(
                publicUrl)
            {
                Host = "127.0.0.1"
            }.Uri;

        using var http =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        8)
            };

        var html =
            await http.GetStringAsync(
                localPage);

        Require(
            html.Contains(
                "PRE-ROUND WIN PLAN",
                StringComparison.Ordinal),
            "Phone dashboard must render the pre-round win plan.");

        Require(
            html.Contains(
                "ENEMY READ",
                StringComparison.Ordinal),
            "Phone dashboard must render opponent DNA.");

        Require(
            html.Contains(
                "LAN FALLBACK",
                StringComparison.Ordinal),
            "Phone dashboard must contain the HTTP wake-lock fallback.");

        var token =
            publicUrl.Query
                .TrimStart('?')
                .Split(
                    '&',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(x =>
                    x.Split(
                        '=',
                        2))
                .Where(x =>
                    x.Length == 2 &&
                    x[0] == "token")
                .Select(x =>
                    Uri.UnescapeDataString(
                        x[1]))
                .FirstOrDefault();

        Require(
            !string.IsNullOrWhiteSpace(
                token),
            "Phone dashboard URL must contain an access token.");

        var stateUrl =
            $"http://127.0.0.1:{server.Port}/api/state?token={Uri.EscapeDataString(token!)}";

        var stateJson =
            await http.GetStringAsync(
                stateUrl);

        using var doc =
            JsonDocument.Parse(
                stateJson);

        var root =
            doc.RootElement;

        Require(
            root.GetProperty("side").GetString() == "CT",
            "Phone API must expose the current side.");

        Require(
            root.GetProperty("round").GetString() == "R5",
            "Phone API must expose the next human-readable round.");

        Require(
            root.GetProperty("routeLabel").GetString() == "START POSITION",
            "CT phone plan must use START POSITION.");

        Require(
            !string.IsNullOrWhiteSpace(
                root.GetProperty("position").GetString()),
            "Phone API must expose a predictive position.");

        Require(
            root.TryGetProperty(
                "enemyRead",
                out var enemyRead) &&
            !string.IsNullOrWhiteSpace(
                enemyRead.GetString()),
            "Phone API must expose ENEMY READ.");

        Require(
            root.TryGetProperty(
                "planMode",
                out var planMode) &&
            !string.IsNullOrWhiteSpace(
                planMode.GetString()),
            "Phone API must expose confidence-aware plan mode.");
    }

    private static async Task TestRealDemoFormatsAsync(
        string demoPath)
    {
        Require(
            File.Exists(demoPath),
            "Pinned real demo fixture is missing.");

        var tempDir =
            Path.Combine(
                Path.GetTempPath(),
                "smoki-v7-ci-" +
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(
            tempDir);

        try
        {
            await TestOneDemoAsync(
                demoPath,
                "raw");

            var gzipPath =
                Path.Combine(
                    tempDir,
                    "fixture.dem.gz");

            await using (var input =
                         File.OpenRead(demoPath))
            await using (var output =
                         File.Create(gzipPath))
            await using (var gzip =
                         new GZipStream(
                             output,
                             CompressionLevel.Fastest))
            {
                await input.CopyToAsync(
                    gzip);
            }

            await TestOneDemoAsync(
                gzipPath,
                "gzip");

            var zipPath =
                Path.Combine(
                    tempDir,
                    "fixture.zip");

            using (var archive =
                   ZipFile.Open(
                       zipPath,
                       ZipArchiveMode.Create))
            {
                var entry =
                    archive.CreateEntry(
                        "fixture.dem",
                        CompressionLevel.Fastest);

                await using var input =
                    File.OpenRead(demoPath);

                await using var output =
                    entry.Open();

                await input.CopyToAsync(
                    output);
            }

            await TestOneDemoAsync(
                zipPath,
                "zip");
        }
        finally
        {
            try
            {
                Directory.Delete(
                    tempDir,
                    true);
            }
            catch { }
        }
    }

    private static async Task TestOneDemoAsync(
        string demoPath,
        string format)
    {
        var report =
            await DemoGroundTruthAnalyzer.AnalyzeSourceAsync(
                demoPath,
                "ci-smoke-demo-" + format,
                DateTime.UtcNow,
                "__smoki_v7_smoke_no_player__",
                null,
                null);

        Require(
            !string.IsNullOrWhiteSpace(report.Map),
            $"{format}: real demo parser did not resolve a map.");

        Require(
            report.Map.StartsWith(
                "de_",
                StringComparison.OrdinalIgnoreCase),
            $"{format}: unexpected CS2 map name: '{report.Map}'.");
    }

    private static GroundTruthPathSample Sample(
        string place,
        string zone)
        => new()
        {
            PlaceName = place,
            Zone = zone
        };

    private static void Require(
        bool condition,
        string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
