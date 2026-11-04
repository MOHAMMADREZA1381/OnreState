using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Tests;

public class LeaderboardTests
{
    private const string Wallet = "6GaFaUBBUygeiKtiWonbAPkPvr3CezNeQcUquEfDZnU8";

    // Shape of the real response (values from a real call; the breakdown is trimmed because the bot does not read it here)
    private const string PageJson = """
    {"leaderboard":{"content":[
      {"rank":1,"address":"DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh","totalPoints":19669966829,"pointsBreakdown":{"wallet":3030277,"kamino":{"onyc":5716127427}}},
      {"rank":2,"address":"E3NiM5XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXn6yMgp","totalPoints":15287032683,"pointsBreakdown":{}}],
     "empty":false,"first":true,"last":false,"number":0,"numberOfElements":2,"size":2,"totalElements":25079,"totalPages":12540}}
    """;

    private static LeaderboardEntry Entry(int rank, decimal points, string? address = null) =>
        new() { Rank = rank, TotalPoints = points, Address = address ?? $"Wa11et{rank:D4}AddressForTestsOnly000000000000" };

    private static LeaderboardPosition Position() => new()
    {
        Rank = 1380,
        Points = 24_099_818m,
        TotalUsers = 25_079,
        Neighbours = { Entry(1378, 24_150_000m), Entry(1379, 24_120_000m), Entry(1380, 24_099_818m, Wallet), Entry(1381, 24_090_000m), Entry(1382, 24_080_000m) },
        NextRankPoints = 24_120_000m,
        Goals = { Entry(1000, 30_000_000m), Entry(500, 60_000_000m) }
    };

    // ---------- reading the API ----------

    [Fact]
    public void Leaderboard_IsReadFromTheRealResponseShape()
    {
        var page = OnReClient.ParseLeaderboard(PageJson);

        Assert.Equal(25_079, page.TotalElements);
        Assert.Equal(2, page.Content.Count);
        Assert.Equal(1, page.Content[0].Rank);
        Assert.Equal("DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh", page.Content[0].Address);
        Assert.Equal(19_669_966_829m, page.Content[0].TotalPoints);
    }

    [Theory]
    [InlineData("0.1102", "0.1102")]
    [InlineData("285007636.140749493215127560", "285007636.140749493215127560")]
    [InlineData("  1.162479014\n", "1.162479014")]
    public void StatsNumbers_AreBareDecimals(string text, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), OnReClient.ParseNumber(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("<html>error</html>")]
    public void StatsNumbers_BadOrNonPositiveAreUnknown(string text)
    {
        Assert.Null(OnReClient.ParseNumber(text));
    }

    // ---------- Top x% in the summary ----------

    [Fact]
    public void Summary_ShowsTopPercentWhenTheNumberOfWalletsIsKnown()
    {
        var text = MessageFormatter.FormatSummary(new OnReResponse { Rank = 1380, TotalPoints = 24_099_818m, Address = Wallet }, false, 25_079);

        Assert.Contains("🥇 Rank: <b>#1,380</b> · Top 5.5% of 25,079", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Summary_WithoutTheTotalKeepsTheOldRankLine()
    {
        var text = MessageFormatter.FormatSummary(new OnReResponse { Rank = 1380, TotalPoints = 24_099_818m, Address = Wallet }, false);

        Assert.Contains("🥇 Rank: <b>#1,380</b>", text);
        Assert.DoesNotContain("Top ", text);
    }

    [Theory]
    [InlineData(1, 25079, "Top 0.1%")]       // never 0
    [InlineData(1380, 25079, "Top 5.5%")]
    [InlineData(25079, 25079, "Top 100.0%")]
    public void TopPercent_IsRoundedAndNeverZero(int rank, long total, string expected)
    {
        var text = MessageFormatter.FormatSummary(new OnReResponse { Rank = rank, TotalPoints = 5m, Address = Wallet }, false, total);

        Assert.Contains(expected, text);
    }

    // ---------- leaderboard screen ----------

    [Fact]
    public void Board_ListsTheTopWalletsShortenedAndAligned()
    {
        var page = OnReClient.ParseLeaderboard(PageJson);

        var text = MessageFormatter.FormatLeaderboard(page, false);

        Assert.Contains("🏅 <b>OnRe Leaderboard</b>", text);
        Assert.Matches(@"#1\s+DTjNdf…Snvh\s+19,669,966,829", text);
        Assert.Contains("25,079 wallets have points", text);
        var rows = text.Split('\n').Select(l => l.Replace("<pre>", "").Replace("</pre>", "").TrimEnd('\r')).Where(l => l.StartsWith("#")).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Length, rows[1].Length);   // same width: the points are right-aligned
        Assert.True(rows[0].Length <= 42, "a wider block scrolls sideways on a phone");
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Board_FailureOrEmptySaysUnavailable()
    {
        Assert.Contains("currently unavailable", MessageFormatter.FormatLeaderboard(null, true));
        Assert.Contains("currently unavailable", MessageFormatter.FormatLeaderboard(new LeaderboardPage(), false));
    }

    // ---------- my position ----------

    [Fact]
    public void Position_ShowsRankTopPercentAndMarksTheWalletsOwnRow()
    {
        var text = MessageFormatter.FormatPosition(Position(), Wallet, false, false);

        Assert.Contains("🥇 <b>#1,380</b> of 25,079 · <b>Top 5.5%</b>", text);
        var own = text.Split('\n').Single(l => l.Contains("◀ you"));
        Assert.Contains("#1,380", own);
        Assert.Contains("24,099,818", own);
        Assert.Equal(1, text.Split("◀ you").Length - 1);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Position_ShowsThePointsMissingToThePlaceAboveAndToTheGoals()
    {
        var text = MessageFormatter.FormatPosition(Position(), Wallet, false, false);

        Assert.Contains("To pass #1,379: <b>+20,182</b> points", text);      // 24,120,000 - 24,099,818
        Assert.Contains("To reach #1,000: <b>+5,900,182</b> points", text);  // 30,000,000 - 24,099,818
        Assert.Contains("To reach #500: <b>+35,900,182</b> points", text);
        Assert.Contains("Everyone keeps earning points", text);
    }

    [Fact]
    public void Position_TheTopWalletSimplyIsNumberOne()
    {
        var position = new LeaderboardPosition { Rank = 1, Points = 5m, TotalUsers = 100, Neighbours = { Entry(1, 5m, Wallet), Entry(2, 4m) } };

        var text = MessageFormatter.FormatPosition(position, Wallet, false, false);

        Assert.Contains("You are #1", text);
        Assert.DoesNotContain("To pass", text);
    }

    [Fact]
    public void Position_LevelWithThePlaceAboveIsNotANegativeGap()
    {
        var position = Position();
        position.NextRankPoints = position.Points;   // the list moved since the points were read

        var text = MessageFormatter.FormatPosition(position, Wallet, false, false);

        Assert.Contains("level with #1,379", text);
        Assert.DoesNotContain("+0", text);
    }

    [Fact]
    public void Position_WalletWithoutPointsAndFailuresAreExplained()
    {
        Assert.Contains("no OnRe points yet", MessageFormatter.FormatPosition(null, Wallet, false, noPoints: true));
        Assert.Contains("currently unavailable", MessageFormatter.FormatPosition(null, Wallet, failed: true, noPoints: false));
    }

    [Fact]
    public void Position_RowsFitAPhoneScreenEvenWithBigNumbers()
    {
        var position = new LeaderboardPosition
        {
            Rank = 25_000, Points = 12m, TotalUsers = 25_079,
            Neighbours = { Entry(24_999, 19_669_966_829m), Entry(25_000, 12m, Wallet) }   // the biggest number on the real leaderboard
        };

        var text = MessageFormatter.FormatPosition(position, Wallet, false, false);

        var rows = text.Split('\n').Where(l => l.Contains("…")).Select(l => l.Replace("<pre>", "").Replace("</pre>", "").TrimEnd('\r')).ToList();
        Assert.All(rows, r => Assert.True(r.Length <= 42, $"row is {r.Length} characters wide"));
    }

    // ---------- Markets header and buttons ----------

    [Fact]
    public void Markets_ShowOnresOfficialNavApyAndAum()
    {
        var overview = new ExponentOverview { Markets = { new MarketInfo { Name = "ONyc 10JAN27", Maturity = DateTimeOffset.Parse("2027-01-10T00:00:00Z") } } };
        var stats = new OnReStats { Nav = 1.162479014m, Apy = 0.1102m, Aum = 285_007_636.14m };

        var text = MessageFormatter.FormatMarkets(overview, false, DateTimeOffset.Parse("2026-11-04T00:00:00Z"), stats);

        Assert.Contains("🏛 <b>OnRe</b> · NAV <b>$1.1625</b> · APY <b>11.02%</b> · AUM <b>$285.0M</b>", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Markets_StatsLineLeavesOutWhatIsUnknownAndIsAbsentWithoutStats()
    {
        var overview = new ExponentOverview();

        Assert.Contains("NAV <b>$1.1625</b>", MessageFormatter.FormatMarkets(overview, false, DateTimeOffset.UtcNow, new OnReStats { Nav = 1.162479014m }));
        Assert.DoesNotContain("APY <b>", MessageFormatter.FormatMarkets(overview, false, DateTimeOffset.UtcNow, new OnReStats { Nav = 1.162479014m }));
        Assert.DoesNotContain("🏛", MessageFormatter.FormatMarkets(overview, false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Buttons_LeaderboardAndMyPositionExistAndFitTheCallbackLimit()
    {
        var start = ReportButtons.StartKeyboard().InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Contains(start, b => b.CallbackData == ReportButtons.Board);

        var wallet = new string('A', 44);
        var data = ReportButtons.PositionData(wallet);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(ReportButtons.TryParsePosition(data, out var parsed));
        Assert.Equal(wallet, parsed);
        Assert.False(ReportButtons.TryParsePosition("pos:nope", out _));

        var summary = ReportButtons.Keyboard(Wallet).InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Contains(summary, b => b.CallbackData == ReportButtons.PositionData(Wallet));
    }
}
