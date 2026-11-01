using System.Text;
using OnReBot.Clients;
using OnReBot.Config;
using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

// Step 3: history taken from the platforms' own APIs. Sample values come from real API responses.
public class HistoryTests
{
    private const string Wallet = "TestWa11etAddressForUnitTestsJust1111";

    // ---------- buttons ----------

    [Fact]
    public void HistoryButtonData_RoundTripsAndFits64Bytes()
    {
        var longest = new string('1', 44);

        Assert.True(ReportButtons.TryParseHistory(ReportButtons.HistoryData(Wallet), out var parsed));
        Assert.Equal(Wallet, parsed);
        Assert.True(Encoding.UTF8.GetByteCount(ReportButtons.HistoryData(longest)) <= 64);
    }

    [Fact]
    public void FullAndHistoryButtons_AreNotMixedUp()
    {
        Assert.False(ReportButtons.TryParseHistory(ReportButtons.FullData(Wallet), out _));
        Assert.False(ReportButtons.TryParseFull(ReportButtons.HistoryData(Wallet), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("hist:")]
    [InlineData("hist:abc")]
    [InlineData("HIST:TestWa11etAddressForUnitTestsJust1111")]
    public void HistoryButtonData_RejectsAnythingElse(string? data) =>
        Assert.False(ReportButtons.TryParseHistory(data, out _));

    [Fact]
    public void HistoryKeyboard_HasOneButton()
    {
        var button = Assert.Single(ReportButtons.HistoryKeyboard(Wallet).InlineKeyboard.SelectMany(row => row));

        Assert.Equal("📈 History", button.Text);
        Assert.Equal(ReportButtons.HistoryData(Wallet), button.CallbackData);
    }

    // ---------- Kamino ----------

    private const string KaminoJson = """
        {"obligation":"x","history":[
          {"timestamp":"2026-10-31T00:00:00.000Z","refreshedStats":{"netAccountValue":"11.61147989"}},
          {"timestamp":"2026-10-29T00:00:00.000Z","refreshedStats":{"netAccountValue":"11.6048304"}},
          {"timestamp":"2026-11-01T00:00:00.000Z","refreshedStats":{"netAccountValue":"11.61480602"}},
          {"timestamp":"2026-10-30T00:00:00.000Z","refreshedStats":{"netAccountValue":"11.6081547"}}
        ]}
        """;

    [Fact]
    public void Kamino_SummarizesFirstAndLastRegardlessOfOrder()
    {
        var h = KaminoClient.BuildHistory("OnRe Market", KaminoJson);

        Assert.NotNull(h);
        Assert.Equal(4, h!.Points);
        Assert.Equal(11.6048304m, h.FirstNetValueUsd);
        Assert.Equal(11.61480602m, h.LastNetValueUsd);
        Assert.Equal(new DateTime(2026, 10, 29), h.From.UtcDateTime.Date);
        Assert.Equal(new DateTime(2026, 11, 1), h.To.UtcDateTime.Date);
    }

    [Fact]
    public void Kamino_NoSnapshotsMeansNoHistory()
    {
        Assert.Null(KaminoClient.BuildHistory("OnRe Market", """{"history":[]}"""));
        Assert.Null(KaminoClient.BuildHistory("OnRe Market", "{}"));
    }

    // ---------- Exponent ----------

    private const string TradesJson = """
        {"trades":[
          {"at":"2026-10-26T12:44:29+00:00","vault":"7f1P","name":"ONyc 10JAN27","side":"sell","yt":1250,"sy":31.45,"price":0.0251606,"valueUsd":31.45},
          {"at":"2026-10-22T01:48:18+00:00","vault":"7f1P","name":"ONyc 10JAN27","side":"buy","yt":61.57,"sy":1.614,"price":0.0262168,"valueUsd":1.61}
        ],"totalTrades":22,"failed":false}
        """;

    [Fact]
    public void ExponentHistory_ParsesTradesAndTotals()
    {
        var h = HttpExponentPositionProvider.ParseHistory(TradesJson, new());

        Assert.False(h.Failed);
        Assert.Equal(22, h.TotalTrades);
        Assert.Equal(2, h.Trades.Count);
        Assert.False(h.Trades[0].IsBuy);
        Assert.Equal(1250m, h.Trades[0].Yt);
        Assert.True(h.Trades[1].IsBuy);
        Assert.Equal("ONyc 10JAN27", h.Trades[0].Market);
    }

    [Fact]
    public void ExponentHistory_ConfigNameWinsOverServiceName()
    {
        var markets = new List<ExponentMarketConfig> { new() { Name = "ONYC-JAN2027", Vault = "7f1P" } };

        var h = HttpExponentPositionProvider.ParseHistory(TradesJson, markets);

        Assert.All(h.Trades, t => Assert.Equal("ONYC-JAN2027", t.Market));
    }

    [Fact]
    public void ExponentHistory_FailedFlagIsPassedThrough() =>
        Assert.True(HttpExponentPositionProvider.ParseHistory("""{"trades":[],"totalTrades":0,"failed":true}""", new()).Failed);

    // ---------- message ----------

    private static HistoryReport Sample() => new()
    {
        Kamino = { KaminoClient.BuildHistory("OnRe Market", KaminoJson)! },
        Exponent = HttpExponentPositionProvider.ParseHistory(TradesJson, new())
    };

    private static ExponentTrade Trade(DateTimeOffset at, bool buy = true, decimal yt = 100) =>
        new() { At = at, Market = "ONyc 10JAN27", IsBuy = buy, Yt = yt, Sy = yt / 40, Price = 0.025m, ValueUsd = yt * 0.029m };

    // a data row of the trades table starts with the date: "26 Oct 2026  ..."
    private static List<string> TableRows(string text) =>
        text.Split('\n')
            .Select(l => l.Replace("</pre>", "").TrimEnd('\r'))   // the last row of a block shares its line with the closing tag
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d{1,2} [A-Z][a-z]{2} \d{4}  "))
            .ToList();

    [Fact]
    public void Message_ShowsKaminoChangeInAnAlignedBlockWithFullDates()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.Contains("📈 <b>History</b>", text);
        Assert.Contains("🏦 <b>Kamino</b> · <i>OnRe Market</i>", text);
        Assert.Contains("<pre>", text);
        Assert.Matches(@"Net value\s+\$11\.6048 → \$11\.6148", text);
        Assert.Matches(@"Change\s+\+\$0\.0100 \(\+0\.09%\)", text);
        Assert.Matches(@"Period\s+29 Oct 2026 → 1 Nov 2026 \(4 daily snapshots\)", text);
    }

    [Fact]
    public void Message_ShowsASummaryBlockPerMarketWithUsdTotals()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.Contains("🌾 <b>Exponent</b> · YT trades · <i>ONyc 10JAN27</i>", text);
        Assert.Matches(@"Trades\s+2 \(1 buys, 1 sells\)", text);
        Assert.Matches(@"Bought\s+61\.57 YT for \$1\.61", text);
        Assert.Matches(@"Sold\s+1,250\.00 YT for \$31\.45", text);
        Assert.Matches(@"Net\s+-1,188\.43 YT", text);
        Assert.Matches(@"Net received\s+\$29\.84", text);   // paid 1.61, received 31.45
    }

    [Fact]
    public void Message_ShowsTradesInATableWithUsdColumnHeaderAndSeparator()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.Matches(@"Date\s+YT amount\s+Price\s+USD", text);
        Assert.Matches(@"-{11}  -{9}  -{6}  -{5}", text);   // the separator line under the header
        Assert.Matches(@"26 Oct 2026\s+-1,250\.00\s+0\.0252\s+31\.45", text);    // sold: minus
        Assert.Matches(@"22 Oct 2026\s+\+61\.57\s+0\.0262\s+1\.61", text);       // bought: plus
        Assert.Contains("Showing all 2 trades.", text);
    }

    [Fact]
    public void Message_ExplainsTheColumnsInOneLegend()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.Contains("YT amount: + bought, - sold (Net is their sum). Price: USD per YT. USD: value of the trade. Net spent: USD paid for buys minus USD received from sells.", text);
        Assert.Equal(1, text.Split("YT amount: + bought").Length - 1);   // once, not once per market
    }

    [Fact]
    public void Message_TableColumnsLineUpExactlyAndFitAPhoneScreen()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        var rows = TableRows(text);

        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Length, rows[1].Length);   // same width, so the numbers are right-aligned under each other
        Assert.True(rows[0].Length <= 42, $"row is {rows[0].Length} characters wide: a wider block scrolls sideways on a phone");
    }

    [Fact]
    public void Message_NetEqualsBoughtMinusSoldAndZeroHasNoSign()
    {
        var even = new HistoryReport
        {
            Exponent = new ExponentHistory
            {
                Trades = { Trade(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), buy: true, yt: 500), Trade(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), buy: false, yt: 500) },
                TotalTrades = 2
            }
        };
        var more = new HistoryReport { Exponent = new ExponentHistory { Trades = { Trade(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), buy: true, yt: 41372.7m) }, TotalTrades = 1 } };

        Assert.Matches(@"Net\s+0\.00 YT", MessageFormatter.FormatHistory(even));
        Assert.Matches(@"Net\s+\+41,372\.70 YT", MessageFormatter.FormatHistory(more));
    }

    [Fact]
    public void Message_NetSpentIsBoughtMinusSoldInUsd_NotTheGrossPurchases()
    {
        // 500 YT bought for $14.50, then 200 YT sold for $5.80: only $8.70 of real money is in
        var report = new HistoryReport
        {
            Exponent = new ExponentHistory
            {
                Trades = { Trade(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), buy: true, yt: 500), Trade(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), buy: false, yt: 200) },
                TotalTrades = 2
            }
        };

        var text = MessageFormatter.FormatHistory(report);

        Assert.Matches(@"Bought\s+500\.00 YT for \$14\.50", text);
        Assert.Matches(@"Sold\s+200\.00 YT for \$5\.80", text);
        Assert.Matches(@"Net spent\s+\$8\.70", text);
    }

    [Fact]
    public void Message_TradesAreListedNewestFirst()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.True(text.IndexOf("26 Oct 2026", StringComparison.Ordinal) < text.IndexOf("22 Oct 2026", StringComparison.Ordinal));
    }

    [Fact]
    public void Message_ListsAllTradesWhenThereAreNotTooMany()
    {
        var trades = Enumerable.Range(1, 22).Select(i => Trade(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i % 20))).ToList();
        var report = new HistoryReport { Exponent = new ExponentHistory { Trades = trades, TotalTrades = 22 } };

        var text = MessageFormatter.FormatHistory(report);

        Assert.Equal(22, TableRows(text).Count);
        Assert.Contains("Showing all 22 trades.", text);
    }

    [Fact]
    public void Message_ListsAtMostThirtyTradesPerMarketButTotalsCoverAll()
    {
        var trades = Enumerable.Range(1, 40).Select(i => Trade(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i))).ToList();
        var report = new HistoryReport { Exponent = new ExponentHistory { Trades = trades, TotalTrades = 40 } };

        var text = MessageFormatter.FormatHistory(report);

        Assert.Equal(30, TableRows(text).Count);
        Assert.Contains("Showing the latest 30 of 40 trades.", text);
        Assert.Matches(@"Trades\s+40 \(40 buys, 0 sells\)", text);   // the totals still cover all 40
        Assert.Matches(@"Bought\s+4,000\.00 YT for \$116\.00", text);
        Assert.Matches(@"Net spent\s+\$116\.00", text);
    }

    [Fact]
    public void Message_AlwaysShowsDayMonthAndYear()
    {
        var report = new HistoryReport
        {
            Exponent = new ExponentHistory
            {
                Trades = { Trade(new DateTimeOffset(2025, 10, 26, 0, 0, 0, TimeSpan.Zero)), Trade(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero)) },
                TotalTrades = 2
            }
        };

        var text = MessageFormatter.FormatHistory(report);

        Assert.Contains("26 Oct 2025", text);
        Assert.Contains("5 Mar 2026", text);
    }

    [Fact]
    public void Message_SaysWhichPlatformsHaveNoHistoryInAQuote()
    {
        var text = MessageFormatter.FormatHistory(Sample());

        Assert.Contains("<blockquote>", text);
        Assert.Contains("do not provide history through their APIs.", text);
    }

    [Fact]
    public void Message_SingleSnapshotDoesNotInventATrend()
    {
        var one = new KaminoHistory { MarketName = "OnRe Market", From = DateTimeOffset.Parse("2026-11-01T00:00:00Z"), To = DateTimeOffset.Parse("2026-11-01T00:00:00Z"), FirstNetValueUsd = 11.6m, LastNetValueUsd = 11.6m, Points = 1 };

        var text = MessageFormatter.FormatHistory(new HistoryReport { Kamino = { one } });

        Assert.Matches(@"Since\s+1 Nov 2026 \(first snapshot\)", text);
        Assert.DoesNotContain("→", text);
    }

    [Fact]
    public void Message_NegativeChangeIsShownWithMinusAndPercent()
    {
        var down = new KaminoHistory { MarketName = "m", From = DateTimeOffset.Parse("2026-10-01T00:00:00Z"), To = DateTimeOffset.Parse("2026-10-05T00:00:00Z"), FirstNetValueUsd = 100m, LastNetValueUsd = 95.5m, Points = 5 };

        var text = MessageFormatter.FormatHistory(new HistoryReport { Kamino = { down } });

        Assert.Matches(@"\$100\.0000 → \$95\.5000", text);
        Assert.Matches(@"Change\s+-\$4\.5000 \(-4\.50%\)", text);
    }

    [Fact]
    public void Message_FailedPartsAreMarkedUnavailableAndTheRestStays()
    {
        var report = Sample();
        report.KaminoFailed = true;
        report.Exponent = new ExponentHistory { Failed = true };

        var text = MessageFormatter.FormatHistory(report);

        Assert.Contains("<b>Kamino</b>", text);
        Assert.Contains("<b>Exponent</b>", text);
        Assert.Equal(2, text.Split("currently unavailable").Length - 1);
        Assert.DoesNotContain("No history found", text);
        Assert.Contains("do not provide history", text);
    }

    [Fact]
    public void Message_EmptyWalletSaysNoHistory()
    {
        var text = MessageFormatter.FormatHistory(new HistoryReport());

        Assert.Contains("<b>No history found</b> for this wallet.", text);
        Assert.Contains("do not provide history", text);
    }

    // ---------- Telegram size limit ----------

    [Fact]
    public void FitToTelegram_LeavesShortMessagesAlone() =>
        Assert.Equal("<b>short</b>", MessageFormatter.FitToTelegram("<b>short</b>"));

    [Fact]
    public void FitToTelegram_CutsLongMessagesAtABlankLineAndSaysSo()
    {
        var section = "<b>block</b> " + new string('x', 400);
        var text = string.Join("\n\n", Enumerable.Repeat(section, 20));   // about 8000 characters

        var fitted = MessageFormatter.FitToTelegram(text);

        Assert.True(fitted.Length <= MessageFormatter.TelegramLimit);
        Assert.Contains("message shortened", fitted);
        Assert.Null(TelegramHtml.Validate(fitted));   // still valid HTML: it was cut between sections
    }
}
