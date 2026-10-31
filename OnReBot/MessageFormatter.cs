using System.Globalization;
using System.Net;
using System.Text;
using OnReBot.Models;

namespace OnReBot;

public static class MessageFormatter
{
    private const string Unavailable = "currently unavailable";
    private const string Divider = "━━━━━━━━━━━━━━━━";

    public static string Format(PortfolioReport report)
    {
        // OnRe returns a rank even for addresses it has never seen (with 0 points), so rank alone means nothing.
        var noPoints = !report.OnReFailed && report.OnRe is { TotalPoints: 0 };
        var anyFailed = report.OnReFailed || report.KaminoFailed || report.LoopscaleFailed || report.ExponentFailed;
        var noPositions = report.Kamino.Count == 0 && report.Loopscale.Count == 0 && report.Exponent.Count == 0;
        if (noPoints && noPositions && !anyFailed)
            return "🔎 <b>No activity found for this wallet.</b>\nDouble-check the address and try again.";

        var sb = new StringBuilder();

        sb.AppendLine("🏆 <b>OnRe Rank Report</b>");
        sb.AppendLine(Divider);

        if (report.OnReFailed)
        {
            sb.AppendLine($"📊 OnRe points: <i>{Unavailable}</i>");
        }
        else if (noPoints)
        {
            sb.AppendLine("📊 OnRe points: <i>none yet</i>");
        }
        else if (report.OnRe != null)
        {
            sb.AppendLine($"👛 Wallet: <code>{Esc(Short(report.OnRe.Address))}</code>");
            sb.AppendLine($"🥇 Rank: <b>#{Num(report.OnRe.Rank)}</b>");
            sb.AppendLine($"⭐ Total points: <b>{Num(report.OnRe.TotalPoints)}</b>");
            sb.AppendLine();
            sb.AppendLine("📊 <b>Points by platform</b>");
            AppendPlatformPoints(sb, report.OnRe.PointsBreakdown);
        }

        sb.AppendLine();
        sb.AppendLine("💼 <b>Active positions</b>");

        var anyPosition = false;

        if (report.KaminoFailed)
            sb.AppendLine($"\n🔹 <b>Kamino</b>\n   <i>{Unavailable}</i>");
        foreach (var k in report.Kamino)
        {
            anyPosition = true;
            sb.AppendLine($"\n🔹 <b>Kamino</b> · {Esc(k.MarketName)}");
            AppendKamino(sb, k);
        }

        if (report.LoopscaleFailed)
            sb.AppendLine($"\n🔹 <b>Loopscale</b>\n   <i>{Unavailable}</i>");
        foreach (var l in report.Loopscale)
        {
            anyPosition = true;
            sb.AppendLine($"\n🔹 <b>Loopscale</b> · {Esc(l.VaultName)}");
            sb.AppendLine($"   Deposit: {Dec(l.Usdc)} USDC");
            AppendCachedTag(sb, l.CachedSeconds);
        }

        if (report.ExponentFailed)
            sb.AppendLine($"\n🔹 <b>Exponent</b>\n   <i>{Unavailable}</i>");
        foreach (var e in report.Exponent)
        {
            anyPosition = true;
            sb.AppendLine($"\n🔹 <b>Exponent {Esc(e.Section)}</b> · {Esc(e.Market)}");
            foreach (var line in e.Details.Split('\n'))
                sb.AppendLine($"   {Esc(line)}");
            AppendCachedTag(sb, e.CachedSeconds);
        }

        // Protected/Liquidity could not be loaded, so the list above may be incomplete
        if (report.ExponentExtrasFailed && !report.ExponentFailed)
            sb.AppendLine($"\n🔹 <b>Exponent Protected / Liquidity</b>\n   <i>{Unavailable}</i>");

        if (!anyPosition && !report.KaminoFailed && !report.LoopscaleFailed && !report.ExponentFailed && !report.ExponentExtrasFailed)
            sb.AppendLine("\nNo active positions found.");

        return sb.ToString().TrimEnd();
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
                    sb.AppendLine($"    ◦ {Esc(market)}: {Num(marketTotal)}");
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
            sb.AppendLine("• None yet.");
    }

    private static void AppendKamino(StringBuilder sb, KaminoPosition k)
    {
        var tokens = string.Join(" + ", k.Tokens.Select(t => $"{Dec(t.Amount)} {Esc(t.Symbol)}"));
        sb.AppendLine(tokens.Length > 0
            ? $"   Deposit: {tokens} (≈ ${Dec(k.TotalDepositUsd)})"
            : $"   Deposit: ≈ ${Dec(k.TotalDepositUsd)}");

        if (k.TotalBorrowUsd > 0)
        {
            sb.AppendLine($"   Debt: ${Dec(k.TotalBorrowUsd)}");
            sb.AppendLine($"   LTV: {Dec(k.LoanToValue * 100)}%");
        }
        else
        {
            sb.AppendLine("   Debt: none");
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

    private static string Num(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Dec(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Esc(string text) => WebUtility.HtmlEncode(text);
}
