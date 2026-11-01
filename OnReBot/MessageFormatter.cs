using System.Globalization;
using System.Text;
using OnReBot.Models;

namespace OnReBot;

// All messages use Telegram's HTML tags: <b> for titles and values, <i> for hints, <code> for addresses and numbers in lists,
// <pre> for aligned tables and <blockquote> for notes. Dynamic text is always escaped with Esc.
public static class MessageFormatter
{
    private const string Divider = "━━━━━━━━━━━━━━━━";
    private const string Unavailable = "⚠️ <i>currently unavailable</i>";

    private const string NoActivityMessage = "🔎 <b>No activity found</b> for this wallet.\n<i>Double-check the address and try again.</i>";

    // Asked after the quick summary. The note explains why some data can be missing.
    public const string FullReportPrompt =
        "<blockquote>Want your <b>positions</b> too (Kamino, Loopscale and Exponent, including PnL)?\n"
        + "<i>(All requests are sent from a single IP address, so some data may be temporarily unavailable.)</i></blockquote>";

    // The whole report in one message: rank and points, then the positions.
    public static string Format(PortfolioReport report)
    {
        if (IsEmptyWallet(report))
            return NoActivityMessage;

        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendOnReBlock(sb, report.OnReFailed, report.OnRe);
        sb.AppendLine();
        AppendPositions(sb, report);
        return sb.ToString().TrimEnd();
    }

    // Step 1: only rank and points (a single fast request), followed by the question about the full report.
    public static string FormatSummary(OnReResponse? onRe, bool onReFailed)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        AppendOnReBlock(sb, onReFailed, onRe);
        sb.AppendLine();
        sb.AppendLine(FullReportPrompt);
        return sb.ToString().TrimEnd();
    }

    // Step 2: only the positions (rank and points were already shown in step 1).
    public static string FormatPositions(PortfolioReport report)
    {
        if (IsEmptyWallet(report))
            return NoActivityMessage;

        var sb = new StringBuilder();
        AppendPositions(sb, report);
        return sb.ToString().TrimEnd();
    }

    // Telegram rejects messages longer than 4096 characters. Anything built here must fit; this is the safety net for the
    // (unlikely) case that it does not: cut at a blank line (so no tag is left open) and say so.
    public const int TelegramLimit = 4096;

    public static string FitToTelegram(string html)
    {
        if (html.Length <= TelegramLimit)
            return html;

        const string note = "\n\n<i>…message shortened to fit Telegram's size limit.</i>";
        var cut = html.LastIndexOf("\n\n", TelegramLimit - note.Length, StringComparison.Ordinal);
        return (cut > 0 ? html[..cut] : html[..(TelegramLimit - note.Length)]) + note;
    }

    // Step 3: recent changes taken from the platforms' own APIs (the bot stores nothing).
    // Shows as many trades per market as fit in one Telegram message (up to 30).
    public static string FormatHistory(HistoryReport history)
    {
        var text = "";
        foreach (var perMarket in new[] { 30, 20, 10, 5 })
        {
            text = BuildHistory(history, perMarket);
            if (text.Length <= TelegramLimit - 100)
                return text;
        }
        return FitToTelegram(text);
    }

    private static string BuildHistory(HistoryReport history, int maxTradesPerMarket)
    {
        var sb = new StringBuilder();
        sb.AppendLine("📈 <b>History</b>");
        sb.AppendLine(Divider);

        if (history.KaminoFailed)
            sb.AppendLine($"\n🏦 <b>Kamino</b>\n{Unavailable}");
        foreach (var k in history.Kamino)
            AppendKaminoHistory(sb, k);

        if (history.Exponent.Failed)
            sb.AppendLine($"\n🌾 <b>Exponent</b>\n{Unavailable}");
        else
            AppendExponentTrades(sb, history.Exponent, maxTradesPerMarket);

        var nothing = !history.KaminoFailed && history.Kamino.Count == 0 && !history.Exponent.Failed && history.Exponent.Trades.Count == 0;
        if (nothing)
            sb.AppendLine("\n<b>No history found</b> for this wallet.");

        return sb.ToString().TrimEnd();
    }

    // Each platform: a bold title with the market in italics, then a small aligned key/value block.
    private static void AppendKaminoHistory(StringBuilder sb, KaminoHistory k)
    {
        sb.AppendLine($"\n🏦 <b>Kamino</b> · <i>{Esc(k.MarketName)}</i>");

        var rows = new List<(string Label, string Value)>();
        if (k.Points < 2)
        {
            rows.Add(("Net value", $"${Dec4(k.LastNetValueUsd)}"));
            rows.Add(("Since", $"{Day(k.To)} (first snapshot)"));
        }
        else
        {
            var change = k.LastNetValueUsd - k.FirstNetValueUsd;
            var percent = k.FirstNetValueUsd > 0 ? $" ({Sign(change)}{Math.Abs(change / k.FirstNetValueUsd * 100).ToString("0.00", CultureInfo.InvariantCulture)}%)" : "";
            rows.Add(("Net value", $"${Dec4(k.FirstNetValueUsd)} → ${Dec4(k.LastNetValueUsd)}"));
            rows.Add(("Change", $"{Sign(change)}${Dec4(Math.Abs(change))}{percent}"));
            rows.Add(("Period", $"{Day(k.From)} → {Day(k.To)} ({k.Points} daily snapshots)"));
        }
        sb.AppendLine(Pre(AlignRows(rows)));
    }

    // Each market: a short summary (totals, net, money really put in), the latest trades in a table, and the older ones in a
    // collapsed quote that opens on tap. Matured markets are hidden when there is an active one. The table has no "side"
    // column: the sign of the YT amount shows it (+ bought, - sold), which keeps it narrow enough for a phone screen.
    private static void AppendExponentTrades(StringBuilder sb, ExponentHistory history, int maxPerMarket)
    {
        var groups = history.Trades.GroupBy(t => t.Market).ToList();

        foreach (var group in groups)
        {
            var trades = group.OrderByDescending(t => t.At).ToList();
            var buys = trades.Where(t => t.IsBuy).ToList();
            var sells = trades.Where(t => !t.IsBuy).ToList();

            sb.AppendLine($"\n🌾 <b>Exponent</b> · YT trades · <i>{Esc(group.Key)}</i>");

            var summary = new List<(string Label, string Value)>
            {
                ("Trades", $"{trades.Count} ({buys.Count} buys, {sells.Count} sells)")
            };
            if (buys.Count > 0)
                summary.Add(("Bought", $"{Dec(buys.Sum(t => t.Yt))} YT for ${Dec(buys.Sum(t => t.ValueUsd))}"));
            if (sells.Count > 0)
                summary.Add(("Sold", $"{Dec(sells.Sum(t => t.Yt))} YT for ${Dec(sells.Sum(t => t.ValueUsd))}"));
            var net = buys.Sum(t => t.Yt) - sells.Sum(t => t.Yt);
            summary.Add(("Net", Math.Abs(net) < 0.005m ? "0.00 YT" : $"{Sign(net)}{Dec(Math.Abs(net))} YT"));

            // "Bought" counts every purchase, also those paid with the proceeds of earlier sales. The net amount is the money
            // that was really put in (or taken out).
            var spent = buys.Sum(t => t.ValueUsd) - sells.Sum(t => t.ValueUsd);
            summary.Add((spent >= 0 ? "Net spent" : "Net received", $"${Dec(Math.Abs(spent))}"));
            sb.AppendLine(Pre(AlignRows(summary)));

            // Right-aligned number columns inside a <pre> block.
            var shown = trades.Take(maxPerMarket).ToList();
            var dates = shown.Select(t => Day(t.At)).ToList();
            var amounts = shown.Select(t => (t.IsBuy ? "+" : "-") + Dec(t.Yt)).ToList();
            var prices = shown.Select(t => t.Price.ToString("0.0000", CultureInfo.InvariantCulture)).ToList();
            var values = shown.Select(t => Dec(t.ValueUsd)).ToList();
            var dateWidth = Math.Max("Date".Length, dates.Max(d => d.Length));
            var amountWidth = Math.Max("YT amount".Length, amounts.Max(a => a.Length));
            var priceWidth = Math.Max("Price".Length, prices.Max(p => p.Length));
            var valueWidth = Math.Max("USD".Length, values.Max(v => v.Length));

            var header = new[]
            {
                $"{"Date".PadRight(dateWidth)}  {"YT amount".PadLeft(amountWidth)}  {"Price".PadLeft(priceWidth)}  {"USD".PadLeft(valueWidth)}",
                $"{new string('-', dateWidth)}  {new string('-', amountWidth)}  {new string('-', priceWidth)}  {new string('-', valueWidth)}"
            };
            var rows = Enumerable.Range(0, shown.Count)
                .Select(i => $"{dates[i].PadRight(dateWidth)}  {amounts[i].PadLeft(amountWidth)}  {prices[i].PadLeft(priceWidth)}  {values[i].PadLeft(valueWidth)}")
                .ToList();

            sb.AppendLine(Pre(header.Concat(rows)));
            sb.AppendLine(shown.Count < trades.Count
                ? $"<i>Showing the latest {shown.Count} of {trades.Count} trades.</i>"
                : $"<i>Showing all {trades.Count} {(trades.Count == 1 ? "trade" : "trades")}.</i>");
        }

        if (groups.Count > 0)
            sb.AppendLine("\n<blockquote><b>YT amount</b>: + bought, - sold\n<b>Net</b>: sum of the YT amounts\n<b>Price</b>: USD per YT\n<b>USD</b>: value of the trade\n<b>Net spent</b>: USD paid for buys minus USD received from sells</blockquote>");

        if (history.Trades.Count < history.TotalTrades)
            sb.AppendLine($"<i>The totals above cover the latest {history.Trades.Count} of {history.TotalTrades} trades.</i>");
    }

    private static string Sign(decimal value) => value < 0 ? "-" : "+";

    // OnRe returns a rank even for addresses it has never seen (with 0 points), so rank alone means nothing.
    private static bool IsEmptyWallet(PortfolioReport report)
    {
        var noPoints = !report.OnReFailed && report.OnRe is { TotalPoints: 0 };
        var anyFailed = report.OnReFailed || report.KaminoFailed || report.LoopscaleFailed || report.ExponentFailed;
        var noPositions = report.Kamino.Count == 0 && report.Loopscale.Count == 0 && report.Exponent.Count == 0;
        return noPoints && noPositions && !anyFailed;
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("🏆 <b>OnRe Rank Report</b>");
        sb.AppendLine(Divider);
    }

    private static void AppendOnReBlock(StringBuilder sb, bool onReFailed, OnReResponse? onRe)
    {
        var noPoints = !onReFailed && onRe is { TotalPoints: 0 };

        if (onReFailed)
        {
            sb.AppendLine($"📊 <b>OnRe points</b>: {Unavailable}");
        }
        else if (noPoints)
        {
            sb.AppendLine("📊 <b>OnRe points</b>: <i>none yet</i>");
        }
        else if (onRe != null)
        {
            sb.AppendLine($"👛 Wallet: <code>{Esc(Short(onRe.Address))}</code>");
            sb.AppendLine($"🥇 Rank: <b>#{Num(onRe.Rank)}</b>");
            sb.AppendLine($"⭐ Total points: <b>{Num(onRe.TotalPoints)}</b>");
            sb.AppendLine();
            sb.AppendLine("📊 <b>Points by platform</b>");
            AppendPlatformPoints(sb, onRe.PointsBreakdown);
        }
    }

    private static void AppendPositions(StringBuilder sb, PortfolioReport report)
    {
        sb.AppendLine("💼 <b>Active positions</b>");

        var anyPosition = false;

        if (report.KaminoFailed)
            sb.AppendLine($"\n🏦 <b>Kamino</b>\n   {Unavailable}");
        foreach (var k in report.Kamino)
        {
            anyPosition = true;
            sb.AppendLine($"\n🏦 <b>Kamino</b> · {Esc(k.MarketName)}");
            AppendKamino(sb, k);
        }

        if (report.LoopscaleFailed)
            sb.AppendLine($"\n🔁 <b>Loopscale</b>\n   {Unavailable}");
        foreach (var l in report.Loopscale)
        {
            anyPosition = true;
            sb.AppendLine($"\n🔁 <b>Loopscale</b> · {Esc(l.VaultName)}");
            sb.AppendLine($"   Deposit: <b>{Dec(l.Usdc)} USDC</b>");
            AppendCachedTag(sb, l.CachedSeconds);
        }

        if (report.ExponentFailed)
            sb.AppendLine($"\n🌾 <b>Exponent</b>\n   {Unavailable}");
        foreach (var e in report.Exponent)
        {
            anyPosition = true;
            sb.AppendLine($"\n{SectionEmoji(e.Section)} <b>Exponent {Esc(e.Section)}</b> · {Esc(e.Market)}");
            // The "◦" sub-lines (PnL breakdown) go into a collapsed quote; Total PnL stays visible
            var lines = e.Details.Split('\n');
            var breakdown = lines.Where(l => l.StartsWith("  ◦ ", StringComparison.Ordinal)).ToList();
            foreach (var line in lines.Except(breakdown))
                sb.AppendLine($"   {StyleDetail(line)}");
            if (breakdown.Count > 0)
                sb.AppendLine("<blockquote expandable>" + string.Join("\n", breakdown.Select(l => StyleDetail(l.Trim()[2..].Trim()))) + "</blockquote>");
            AppendCachedTag(sb, e.CachedSeconds);
        }

        // Protected/Liquidity could not be loaded, so the list above may be incomplete
        if (report.ExponentExtrasFailed && !report.ExponentFailed)
            sb.AppendLine($"\n🛡 <b>Exponent Protected / Liquidity</b>\n   {Unavailable}");

        if (!anyPosition && !report.KaminoFailed && !report.LoopscaleFailed && !report.ExponentFailed && !report.ExponentExtrasFailed)
            sb.AppendLine("\n<i>No active positions found.</i>");
    }

    private static string SectionEmoji(string section) => section switch
    {
        "Farm" => "🌾",
        "Protected" => "🛡",
        "Liquidity" => "💧",
        _ => "🔹"
    };

    // "Label: value" -> "Label: <b>value</b>". Gains and losses get a 🟢 / 🔴 so they stand out; hints stay italic.
    private static string StyleDetail(string line)
    {
        var split = line.IndexOf(": ", StringComparison.Ordinal);
        if (split < 0)
            return Esc(line);

        var label = Esc(line[..split]);
        var value = line[(split + 2)..];

        var styled = value switch
        {
            _ when value.StartsWith("temporarily", StringComparison.Ordinal) => $"<i>{Esc(value)}</i>",
            _ when value.StartsWith("+$", StringComparison.Ordinal) => $"🟢 <b>{Esc(value)}</b>",
            _ when value.StartsWith("-$", StringComparison.Ordinal) => $"🔴 <b>{Esc(value)}</b>",
            _ => $"<b>{Esc(value)}</b>"
        };
        return $"{label}: {styled}";
    }

    private static void AppendPlatformPoints(StringBuilder sb, OnRePointsBreakdown b)
    {
        var any = false;

        void Line(string name, decimal value)
        {
            if (value == 0) return;
            any = true;
            sb.AppendLine($"• {Esc(name)}: <b>{Num(value)}</b>");
        }

        Line("Permissionless Boost", b.PermissionlessBoost);
        Line("Kamino", b.Kamino.Values.Sum());

        var exponentTotal = b.Exponent.Yt + b.Exponent.Lp;
        Line("Exponent", exponentTotal);
        if (exponentTotal != 0)
        {
            foreach (var (market, points) in b.Exponent.Markets)
            {
                var marketTotal = points.Yt + points.Lp;
                if (marketTotal != 0)
                    sb.AppendLine($"    ◦ {Esc(market)}: <code>{Num(marketTotal)}</code>");
            }
        }

        Line("Loopscale", b.Loopscale.Values.Sum());
        Line("Ratex", b.Ratex);
        Line("Carrot", b.Carrot);
        Line("Carrot Lending", b.CarrotLending);
        Line("Elemental", b.Elemental);
        Line("Exponent Vault", b.ExponentVault);
        Line("OnRe Yield+", b.OnreYieldPlus);
        Line("Orca", b.Orca.Values.Sum());
        Line("Referral bonus", b.ReferralBonus);

        if (!any)
            sb.AppendLine("• <i>None yet.</i>");
    }

    private static void AppendKamino(StringBuilder sb, KaminoPosition k)
    {
        var tokens = string.Join(" + ", k.Tokens.Select(t => $"{Dec(t.Amount)} {Esc(t.Symbol)}"));
        sb.AppendLine(tokens.Length > 0
            ? $"   Deposit: <b>{tokens}</b> (≈ <b>${Dec(k.TotalDepositUsd)}</b>)"
            : $"   Deposit: ≈ <b>${Dec(k.TotalDepositUsd)}</b>");

        if (k.TotalBorrowUsd > 0)
        {
            sb.AppendLine($"   Debt: <b>${Dec(k.TotalBorrowUsd)}</b>");
            sb.AppendLine($"   LTV: <b>{Dec(k.LoanToValue * 100)}%</b>");
        }
        else
        {
            sb.AppendLine("   Debt: <b>none</b>");
        }
    }

    private static void AppendCachedTag(StringBuilder sb, int? cachedSeconds)
    {
        if (cachedSeconds == null) return;
        sb.AppendLine($"   🕒 <i>cached data · {Age(cachedSeconds.Value)} old</i>");
    }

    private static string Age(int seconds) =>
        seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m {seconds % 60}s";

    private static string Short(string address) =>
        address.Length > 12 ? $"{address[..6]}…{address[^4..]}" : address;

    // "Label  value" rows with the labels padded to the same width, for a <pre> block
    private static List<string> AlignRows(List<(string Label, string Value)> rows)
    {
        var width = rows.Max(r => r.Label.Length);
        return rows.Select(r => $"{r.Label.PadRight(width)}  {r.Value}").ToList();
    }

    // Monospace block (Telegram does not allow other tags inside it, so the lines are plain text)
    private static string Pre(IEnumerable<string> lines) => "<pre>" + string.Join("\n", lines.Select(Esc)) + "</pre>";

    private static string Num(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Dec(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    // Kamino net values move by cents per day, so history needs more decimals to be visible.
    private static string Dec4(decimal value) => value.ToString("N4", CultureInfo.InvariantCulture);

    // 26 Oct 2026
    private static string Day(DateTimeOffset when) => when.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    // Telegram only needs & < > escaped in HTML text (WebUtility.HtmlEncode would also turn symbols like → into numeric entities).
    internal static string Esc(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Fallback for when Telegram rejects the HTML: the same text without tags
    public static string ToPlainText(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "")
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
}
