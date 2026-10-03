using System.Drawing;
using System.Windows.Forms;

namespace Sm0kiSoloCoach;

public sealed record MatchReviewInsight(
    string BestTitle,
    string BestDetail,
    string TroubleTitle,
    string TroubleDetail,
    string FocusTitle,
    string FocusDetail,
    int Kills,
    int Deaths,
    double KillsPerRound,
    double SurvivalRate,
    double WinRate,
    int ZeroKillRounds,
    int MultiKillRounds);

public static class MatchReviewAnalyzer
{
    public static MatchReviewInsight Analyze(
        IReadOnlyList<RoundRecord> rounds)
    {
        var list = rounds.ToList();
        if (list.Count == 0)
        {
            return new MatchReviewInsight(
                "NOT ENOUGH DATA",
                "Play a few tracked rounds first.",
                "NOT ENOUGH DATA",
                "The coach needs round history before it can find a weakness.",
                "NEXT MATCH",
                "Keep GSI active for the full match.",
                0, 0, 0, 0, 0, 0, 0);
        }

        var kills = list.Sum(r => r.KillsRound);
        var deaths = list.Sum(r => r.DeathsRound);
        var kpr = (double)kills / list.Count;
        var survival = 100.0 * list.Count(r => r.Survived) / list.Count;
        var knownWins = list.Where(r => r.Won.HasValue).ToList();
        var winRate = knownWins.Count > 0
            ? 100.0 * knownWins.Count(r => r.Won == true) / knownWins.Count
            : 0;
        var zero = list.Count(r => r.KillsRound == 0);
        var multi = list.Count(r => r.KillsRound >= 2);

        var siteGroups = list
            .Where(r => r.Intent is "A" or "B")
            .GroupBy(r => r.Intent)
            .Select(g => new
            {
                Site = g.Key,
                Rounds = g.Count(),
                Wins = g.Count(r => r.Won == true),
                Kills = g.Sum(r => r.KillsRound),
                Deaths = g.Sum(r => r.DeathsRound),
                Survived = g.Count(r => r.Survived)
            })
            .Where(x => x.Rounds >= 2)
            .ToList();

        var bestSite = siteGroups
            .OrderByDescending(x =>
                (double)x.Wins / x.Rounds * 2.0 +
                (double)x.Kills / x.Rounds +
                (double)x.Survived / x.Rounds)
            .FirstOrDefault();

        var troubleSite = siteGroups
            .OrderBy(x =>
                (double)x.Wins / x.Rounds * 2.0 +
                (double)x.Kills / x.Rounds +
                (double)x.Survived / x.Rounds)
            .FirstOrDefault();

        string bestTitle;
        string bestDetail;

        if (bestSite != null)
        {
            bestTitle = $"BEST AREA • {bestSite.Site}";
            bestDetail =
                $"{bestSite.Wins}W/{bestSite.Rounds - bestSite.Wins}L • " +
                $"{(double)bestSite.Kills / bestSite.Rounds:0.00} K/R • " +
                $"{100.0 * bestSite.Survived / bestSite.Rounds:0}% survival";
        }
        else if (multi >= Math.Max(2, list.Count / 5))
        {
            bestTitle = "BEST • IMPACT ROUNDS";
            bestDetail = $"{multi} multi-kill rounds • {kpr:0.00} kills per round.";
        }
        else
        {
            bestTitle = "BEST • STABILITY";
            bestDetail = $"{survival:0}% survival • {kpr:0.00} kills per round.";
        }

        string troubleTitle;
        string troubleDetail;

        if (troubleSite != null &&
            (bestSite == null || troubleSite.Site != bestSite.Site))
        {
            troubleTitle = $"TROUBLE AREA • {troubleSite.Site}";
            troubleDetail =
                $"{troubleSite.Wins}W/{troubleSite.Rounds - troubleSite.Wins}L • " +
                $"{(double)troubleSite.Kills / troubleSite.Rounds:0.00} K/R • " +
                $"{troubleSite.Deaths} deaths";
        }
        else if (zero >= Math.Max(3, list.Count / 3))
        {
            troubleTitle = "TROUBLE • LOW-IMPACT ROUNDS";
            troubleDetail = $"{zero}/{list.Count} rounds ended with 0 kills.";
        }
        else
        {
            troubleTitle = "TROUBLE • SURVIVAL";
            troubleDetail = $"{100.0 - survival:0}% of tracked rounds ended in a death.";
        }

        string focusTitle;
        string focusDetail;

        if (zero >= Math.Max(3, list.Count / 3))
        {
            focusTitle = "NEXT MATCH • TRADEABLE FIRST";
            focusDetail =
                "Reduce isolated first-contact duels. Let the first contact happen, then trade.";
        }
        else if (survival < 40)
        {
            focusTitle = "NEXT MATCH • LIVE LONGER";
            focusDetail =
                "Take one duel, then reposition. Avoid the second dry peek after contact.";
        }
        else if (troubleSite != null)
        {
            focusTitle = $"NEXT MATCH • FIX {troubleSite.Site}";
            focusDetail =
                $"Change the opening position/timing on {troubleSite.Site}; do not repeat the same first angle.";
        }
        else
        {
            focusTitle = "NEXT MATCH • KEEP THE BASE";
            focusDetail =
                "Keep the successful setup, but vary timing and position after each contact.";
        }

        return new MatchReviewInsight(
            bestTitle,
            bestDetail,
            troubleTitle,
            troubleDetail,
            focusTitle,
            focusDetail,
            kills,
            deaths,
            kpr,
            survival,
            winRate,
            zero,
            multi);
    }
}

public sealed class MatchReviewForm : Form
{
    private static readonly Color Bg = Color.FromArgb(10, 11, 13);
    private static readonly Color Orange = Color.FromArgb(255, 156, 44);
    private static readonly Color Green = Color.FromArgb(112, 214, 151);
    private static readonly Color Red = Color.FromArgb(235, 112, 100);
    private static readonly Color Muted = Color.FromArgb(142, 149, 159);

    public MatchReviewForm(
        string map,
        int? ctScore,
        int? tScore,
        IReadOnlyList<RoundRecord> rounds)
    {
        var review = MatchReviewAnalyzer.Analyze(rounds);

        Text = "Sm0ki Solo Coach • Match Review";
        Width = 900;
        Height = 620;
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(26, 22, 26, 22),
            BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var title = new Label
        {
            Text = "MATCH REVIEW\n" +
                   $"{CoachEngine.PrettyMap(map).ToUpperInvariant()}  •  {ctScore?.ToString() ?? "—"}:{tScore?.ToString() ?? "—"}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title, 0, 0);

        header.Controls.Add(new Label
        {
            Text = "POST-MATCH COACH",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Orange,
            TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);
        root.Controls.Add(header, 0, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 8)
        };
        for (int i = 0; i < 5; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        metrics.Controls.Add(Metric("KILLS", review.Kills.ToString()), 0, 0);
        metrics.Controls.Add(Metric("K / ROUND", review.KillsPerRound.ToString("0.00")), 1, 0);
        metrics.Controls.Add(Metric("SURVIVAL", review.SurvivalRate.ToString("0") + "%"), 2, 0);
        metrics.Controls.Add(Metric("0K ROUNDS", review.ZeroKillRounds.ToString()), 3, 0);
        metrics.Controls.Add(Metric("MULTI", review.MultiKillRounds.ToString()), 4, 0);
        root.Controls.Add(metrics, 0, 1);

        var insights = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        insights.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        insights.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        insights.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));

        insights.Controls.Add(InsightCard(review.BestTitle, review.BestDetail, Green), 0, 0);
        insights.Controls.Add(InsightCard(review.TroubleTitle, review.TroubleDetail, Red), 0, 1);
        insights.Controls.Add(InsightCard(review.FocusTitle, review.FocusDetail, Orange), 0, 2);
        root.Controls.Add(insights, 0, 2);

        var close = new Button
        {
            Text = "DONE",
            Width = 120,
            Height = 36,
            Anchor = AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Orange,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_,__) => Close();
        root.Controls.Add(close, 0, 3);

        Controls.Add(root);
    }

    private static Control Metric(string title, string value)
    {
        var card = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            Padding = new Padding(12, 8, 12, 8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 7, FontStyle.Bold),
            ForeColor = Muted
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        card.Controls.Add(layout);
        return card;
    }

    private static Control InsightCard(
        string title,
        string detail,
        Color accent)
    {
        var card = new ModernCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 0, 5),
            Padding = new Padding(18, 14, 18, 14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = accent
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = detail,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.FromArgb(225, 228, 233),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);

        card.Controls.Add(layout);
        return card;
    }
}
