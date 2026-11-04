using System.Globalization;
using System.Text;
using OnReBot.Models;

namespace OnReBot;

// The Time Machine screens: how fast a wallet gains points and who moves the most, from snapshots of OnRe's public leaderboard.
public static partial class MessageFormatter
{
    private const string SnapshotNote = "Based on hourly snapshots of OnRe's public leaderboard.";

    public static string FormatMomentum(MomentumResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📈 <b>Your momentum</b>");
        sb.AppendLine(Divider);

        if (r.Status == MomentumStatus.NotEnoughData)
        {
            sb.AppendLine($"\n⏳ <b>Collecting data</b>\n<blockquote>The bot takes a snapshot of OnRe's public leaderboard every hour. The first comparison is ready in about {Wait(r.WaitFor)}.</blockquote>");
            return sb.ToString().TrimEnd();
        }
        if (r.Status == MomentumStatus.NotOnLeaderboard)
        {
            sb.AppendLine("\n<i>This wallet is not on the OnRe leaderboard yet (no points).</i>");
            return sb.ToString().TrimEnd();
        }

        AppendWindow(sb, r.Day!);
        if (r.Week != null)
            AppendWindow(sb, r.Week);

        if (r.Day!.PointsPerDay > 0 && r.Goals.Count > 0)
        {
            sb.AppendLine($"\n🎯 <b>At the pace of the last {WindowName(r.Day).ToLowerInvariant().Replace("last ", "")}</b>");
            foreach (var (rank, days) in r.Goals)
                sb.AppendLine($"• #{Num(rank)}: {DaysText(days)}");
        }

        sb.AppendLine($"\n<i>{SnapshotNote} Everyone keeps earning points, so the targets move too: a rough guide.</i>");
        sb.AppendLine($"📸 <i>snapshot from {Age((int)Math.Max(0, r.SnapshotAge.TotalSeconds))} ago</i>");
        return sb.ToString().TrimEnd();
    }

    private static void AppendWindow(StringBuilder sb, Momentum m)
    {
        sb.AppendLine($"\n🗓 <b>{WindowName(m)}</b>");
        var rank = m.IsNew
            ? $"new on the leaderboard → #{Num(m.NewRank)}"
            : m.RanksClimbed > 0 ? $"↑{Num(m.RanksClimbed)}  #{Num(m.OldRank)} → #{Num(m.NewRank)}"
            : m.RanksClimbed < 0 ? $"↓{Num(-m.RanksClimbed)}  #{Num(m.OldRank)} → #{Num(m.NewRank)}"
            : $"no change  #{Num(m.NewRank)}";
        var rows = new List<(string Label, string Value)>
        {
            ("Points", $"{(m.PointsGained >= 0 ? "+" : "-")}{Num(Math.Abs(m.PointsGained))}  ({Num(Math.Round(m.PointsPerDay))}/day)"),
            ("Rank", rank)
        };
        sb.AppendLine(Pre(AlignRows(rows)));
        if (m.PointsGained == 0)
            sb.AppendLine("<i>Your points did not change in this window.</i>");
    }

    // "Last 24h" for a window of about a day, "Last 7 days" for about a week, otherwise the real length ("Last 9h")
    private static string WindowName(Momentum m) =>
        m.Window.TotalHours is >= 22 and <= 27 ? "Last 24h"
        : m.Window.TotalDays is >= 6 and <= 9 ? "Last 7 days"
        : $"Last {(int)m.Window.TotalHours}h";

    // 34.2 -> "in ~34 days", 0.4 -> "in less than a day", null -> "not at this pace"
    private static string DaysText(decimal? days) =>
        days == null ? "<i>not at this pace</i>"
        : days < 1 ? "in less than a day"
        : days > 365 ? "in over a year"
        : $"in ~{Num(Math.Ceiling(days.Value))} days";

    private static string Wait(TimeSpan? wait)
    {
        var minutes = (int)Math.Ceiling((wait ?? TimeSpan.FromMinutes(5)).TotalMinutes);
        return minutes >= 120 ? $"{(minutes + 30) / 60} hours" : minutes >= 60 ? "an hour" : $"{Math.Max(minutes, 1)} minutes";
    }

    public static string FormatMovers(MoversResult m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🚀 <b>Movers</b>" + (m.NotEnoughData ? "" : $" · last {(int)Math.Round(m.Window.TotalHours)}h"));
        sb.AppendLine(Divider);

        if (m.NotEnoughData)
        {
            sb.AppendLine("\n⏳ <b>Collecting data</b>\n<blockquote>The bot takes a snapshot of OnRe's public leaderboard every hour. The first comparison is ready about 6 hours after the first snapshot.</blockquote>");
            return sb.ToString().TrimEnd();
        }

        if (m.Gainers.Count > 0)
        {
            var width = m.Gainers.Max(g => Num(g.PointsGained).Length) + 1;
            var rows = m.Gainers.Select((g, i) => $"{(i + 1).ToString().PadLeft(2)} {Short(g.Address)}  {("+" + Num(g.PointsGained)).PadLeft(width)}{(g.IsNew ? " new" : "")}");
            sb.AppendLine("\n💰 <b>Most points gained</b>");
            sb.AppendLine(Pre(rows));
        }

        if (m.Climbers.Count > 0)
        {
            var rows = m.Climbers.Select((c, i) => $"{(i + 1).ToString().PadLeft(2)} {Short(c.Address)}  #{Num(c.OldRank)} → #{Num(c.NewRank)} ↑{Num(c.OldRank - c.NewRank)}");
            sb.AppendLine("\n⬆️ <b>Biggest rank climbers</b> (top 5,000)");
            sb.AppendLine(Pre(rows));
        }

        sb.AppendLine("\n🆕 <b>New in the Top 100</b>");
        if (m.NewInTop100.Count == 0)
            sb.AppendLine("<i>nobody new</i>");
        else
            sb.AppendLine(Pre(m.NewInTop100.Select(n => $"#{Num(n.NewRank).PadRight(3)} {Short(n.Address)}  {(n.IsNew ? "new wallet" : "from #" + Num(n.OldRank))}")));

        var change = m.WalletsNow - m.WalletsBefore;
        sb.AppendLine($"\n<i>{Num(m.WalletsNow)} wallets with points ({(change >= 0 ? "+" : "")}{Num(change)}). {SnapshotNote}</i>");
        sb.AppendLine($"📸 <i>snapshot from {Age((int)Math.Max(0, m.SnapshotAge.TotalSeconds))} ago</i>");
        return sb.ToString().TrimEnd();
    }
}
