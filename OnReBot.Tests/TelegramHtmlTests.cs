using OnReBot.Models;

namespace OnReBot.Tests;

// Telegram rejects a whole message when its HTML is broken ("can't parse entities"), so every message the bot can build
// is checked here: only allowed tags, all tags balanced, nothing nested inside <pre>/<code>, no raw & < >, and the
// 4096 character limit.
internal static class TelegramHtml
{
    private static readonly HashSet<string> Allowed = new() { "b", "i", "u", "s", "code", "pre", "a", "blockquote", "tg-spoiler" };

    // Returns null when valid, otherwise a description of the problem.
    public static string? Validate(string html)
    {
        if (html.Length > 4096)
            return $"message is {html.Length} characters (limit 4096)";

        var open = new Stack<string>();
        var i = 0;
        while (i < html.Length)
        {
            var c = html[i];
            if (c == '<')
            {
                var close = html.IndexOf('>', i);
                if (close < 0) return $"unterminated tag at {i}";

                var inner = html[(i + 1)..close];
                var closing = inner.StartsWith('/');
                var name = (closing ? inner[1..] : inner).Split(' ')[0];
                if (!Allowed.Contains(name)) return $"tag <{name}> is not allowed";

                if (closing)
                {
                    if (open.Count == 0 || open.Pop() != name) return $"unbalanced </{name}> at {i}";
                }
                else
                {
                    if (open.Contains("pre") || open.Contains("code")) return $"<{name}> nested inside pre/code at {i}";
                    if (name == "blockquote" && open.Contains("blockquote")) return $"nested blockquote at {i}";
                    open.Push(name);
                }
                i = close + 1;
                continue;
            }

            if (c == '>') return $"raw '>' at {i}";

            if (c == '&')
            {
                var semicolon = html.IndexOf(';', i);
                if (semicolon < 0 || semicolon - i > 8) return $"raw '&' at {i}";
                var entity = html[i..(semicolon + 1)];
                if (entity is not ("&amp;" or "&lt;" or "&gt;" or "&quot;") && !entity.StartsWith("&#")) return $"unknown entity {entity}";
                i = semicolon + 1;
                continue;
            }

            i++;
        }

        return open.Count == 0 ? null : $"unclosed <{open.Peek()}>";
    }
}

public class TelegramHtmlTests
{
    // ---------- the validator itself ----------

    [Theory]
    [InlineData("<b>ok</b> <i>fine</i> <code>1</code>", true)]
    [InlineData("<blockquote>quote <b>bold</b></blockquote>", true)]
    [InlineData("<blockquote expandable><pre>x</pre></blockquote>", true)]
    [InlineData("<blockquote>a<blockquote>b</blockquote></blockquote>", false)]
    [InlineData("<pre>a &lt; b &amp; c</pre>", true)]
    [InlineData("<b>unclosed", false)]
    [InlineData("<b>wrong</i>", false)]
    [InlineData("<pre><b>nested</b></pre>", false)]
    [InlineData("<code><i>nested</i></code>", false)]
    [InlineData("a < b", false)]
    [InlineData("a > b", false)]
    [InlineData("fish & chips", false)]
    [InlineData("<div>not allowed</div>", false)]
    public void Validator_RecognisesBrokenHtml(string html, bool valid) =>
        Assert.Equal(valid, TelegramHtml.Validate(html) == null);

    [Fact]
    public void Validator_RejectsTooLongMessages() =>
        Assert.NotNull(TelegramHtml.Validate(new string('a', 4097)));

    // ---------- every message the bot can build ----------

    private static void AssertValid(string html) => Assert.True(TelegramHtml.Validate(html) == null, TelegramHtml.Validate(html) + "\n" + html);

    private static OnReResponse Onre()
    {
        var onRe = new OnReResponse { Rank = 1427, TotalPoints = 22543333, Address = "TestWa11etAddressForUnitTestsJust1111" };
        onRe.PointsBreakdown.Exponent.Yt = 20342393;
        onRe.PointsBreakdown.Exponent.Markets["ONYC-JAN2027"] = new() { Yt = 6838325 };
        onRe.PointsBreakdown.ReferralBonus = 946457;
        return onRe;
    }

    private static PortfolioReport FullReport() => new()
    {
        OnRe = Onre(),
        Kamino = { new KaminoPosition { MarketName = "OnRe <Market> & Co", Tokens = { new() { Symbol = "ONyc", Amount = 10m } }, TotalDepositUsd = 11.6m, TotalBorrowUsd = 2m, LoanToValue = 0.17m } },
        Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 5.98m, CachedSeconds = 12 } },
        Exponent =
        {
            new ExponentPosition { Section = "Farm", Market = "ONYC-JAN2027", Details = "YT balance: 41,372.70\nTotal PnL: -$26.24\n  ◦ Price appreciation: -$154.14\n  ◦ Claimed yield: +$203.18\nPnL: temporarily unavailable", CachedSeconds = 40 },
            new ExponentPosition { Section = "Protected", Market = "srONyc", Details = "Tranche: Senior (protected)\nValue: <$0.01" },
            new ExponentPosition { Section = "Liquidity", Market = "srONyc 10JAN27", Details = "Range: 6.00% – 10.00% (in range)\nValue: $5.81" },
        }
    };

    [Fact]
    public void FullReport_IsValidTelegramHtml() => AssertValid(MessageFormatter.Format(FullReport()));

    [Fact]
    public void Summary_IsValidTelegramHtml()
    {
        AssertValid(MessageFormatter.FormatSummary(Onre(), false));
        AssertValid(MessageFormatter.FormatSummary(null, true));
        AssertValid(MessageFormatter.FormatSummary(new OnReResponse { Rank = 5, TotalPoints = 0 }, false));
    }

    [Fact]
    public void Positions_IsValidTelegramHtml_AlsoWhenPartsFailed()
    {
        AssertValid(MessageFormatter.FormatPositions(FullReport()));

        var failed = new PortfolioReport { OnRe = Onre(), KaminoFailed = true, LoopscaleFailed = true, ExponentFailed = true, ExponentExtrasFailed = true };
        AssertValid(MessageFormatter.FormatPositions(failed));
        AssertValid(MessageFormatter.FormatPositions(new PortfolioReport { OnRe = new OnReResponse { Rank = 5, TotalPoints = 0 } }));
        AssertValid(MessageFormatter.FormatPositions(new PortfolioReport { OnRe = Onre() }));
    }

    private static HistoryReport BigHistory(int trades, int markets) => new()
    {
        Kamino = { new KaminoHistory { MarketName = "OnRe Market", From = DateTimeOffset.Parse("2026-10-29T00:00:00Z"), To = DateTimeOffset.Parse("2026-11-01T00:00:00Z"), FirstNetValueUsd = 11.6048m, LastNetValueUsd = 11.6148m, Points = 4 } },
        Exponent = new ExponentHistory
        {
            TotalTrades = trades * markets,
            Trades = Enumerable.Range(0, trades * markets).Select(i => new ExponentTrade
            {
                At = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i),
                Market = $"Market {i % markets}",
                IsBuy = i % 3 != 0,
                Yt = 12_345.67m + i,
                Sy = 300.5m,
                Price = 0.0262m
            }).ToList()
        }
    };

    [Fact]
    public void History_IsValidTelegramHtml()
    {
        AssertValid(MessageFormatter.FormatHistory(BigHistory(22, 1)));
        AssertValid(MessageFormatter.FormatHistory(new HistoryReport()));
        AssertValid(MessageFormatter.FormatHistory(new HistoryReport { KaminoFailed = true, Exponent = new ExponentHistory { Failed = true } }));
    }

    [Fact]
    public void History_StaysUnderTheTelegramLimitEvenForALotOfTrades()
    {
        // 3 markets with 100 trades each: only 30 per market are listed, so the message must still fit in 4096 characters
        var text = MessageFormatter.FormatHistory(BigHistory(100, 3));

        AssertValid(text);
        Assert.True(text.Length < 4096, $"{text.Length} characters");
    }
}
