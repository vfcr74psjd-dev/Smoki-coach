using System.IO.Compression;
using Sm0kiSoloCoach;

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
