using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Tests;

public class MixTests
{
    private static LeaderboardEntry Entry(int rank, Action<OnRePointsBreakdown> set)
    {
        var entry = new LeaderboardEntry { Rank = rank, Address = $"Wa11et{rank:D4}AddressForTestsOnly000000000000" };
        set(entry.PointsBreakdown);
        entry.TotalPoints = entry.PointsBreakdown.Platforms().Sum(p => p.Points);
        return entry;
    }

    private static LeaderboardPage Page(params LeaderboardEntry[] entries) => new() { Content = entries.ToList(), TotalElements = 25_079 };

    private static LeaderboardPage Sample() => Page(
        Entry(1, b => { b.Kamino["onyc"] = 600m; b.Loopscale["usdc"] = 200m; b.Wallet = 100m; }),
        Entry(2, b => { b.Kamino["onyc"] = 100m; b.Exponent.Yt = 100m; b.Exponent.Tranching.Junior = 100m; }),
        Entry(3, b => { b.Carrot = 50m; b.CarrotLending = 50m; b.ReferralBonus = 100m; }));

    // ---------- the points of one wallet ----------

    [Fact]
    public void Platforms_ListsNonZeroPlatformsAndCountsTranchePointsAsExponent()
    {
        var b = new OnRePointsBreakdown();
        b.Kamino["onyc"] = 5m;
        b.Kamino["usdc"] = 7m;
        b.Exponent.Yt = 10m;
        b.Exponent.Lp = 20m;
        b.Exponent.Tranching.Senior = 1m;
        b.Exponent.Tranching.Junior = 2m;
        b.Exponent.Markets["ONYC-JAN2027"] = new ExponentMarketPoints { Yt = 10m, Lp = 20m };   // a split of Yt and Lp: not added again

        var platforms = b.Platforms();

        Assert.Equal([("Kamino", 12m), ("Exponent", 33m)], platforms.ToArray());
    }

    [Fact]
    public void Leaderboard_BreakdownIsReadFromTheRealJsonShape()
    {
        const string json = """
        {"leaderboard":{"content":[{"rank":1,"address":"DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh","totalPoints":100,
          "pointsBreakdown":{"wallet":3,"kamino":{"onyc":40,"usdg":1},"exponent":{"yt":0,"lp":0,"markets":{"SEP-2026":{"yt":0,"lp":0}},"tranching":{"senior":2,"junior":50}},
                             "loopscale":{"onyc":4,"usdc":0},"orca":{"onycUsdc":0},"referralBonus":0}}],"totalElements":1}}
        """;

        var entry = OnReClient.ParseLeaderboard(json).Content.Single();

        Assert.Equal(100m, entry.PointsBreakdown.Platforms().Sum(p => p.Points));   // 3 + 41 + 52 + 4
        Assert.Equal(52m, entry.PointsBreakdown.Exponent.Total);
    }

    // ---------- the mix of a group ----------

    [Fact]
    public void Mix_SumsPointsPerPlatformLargestFirstAndSharesAddUpToOne()
    {
        var mix = PlatformMix.Build(Sample().Content);

        Assert.Equal("Kamino", mix[0].Name);
        Assert.Equal(700m, mix[0].Points);
        Assert.Equal(1.0m, Math.Round(mix.Sum(r => r.Share), 6));
        Assert.Equal(mix.OrderByDescending(r => r.Points).Select(r => r.Name), mix.Select(r => r.Name));
    }

    [Fact]
    public void Mix_CountsHowManyWalletsUseEachPlatformAndMergesCarrotWithCarrotLending()
    {
        var mix = PlatformMix.Build(Sample().Content);

        Assert.Equal(2, mix.Single(r => r.Name == "Kamino").Wallets);
        Assert.Equal(1, mix.Single(r => r.Name == "Loopscale").Wallets);
        var carrot = mix.Single(r => r.Name == "Carrot");
        Assert.Equal(100m, carrot.Points);
        Assert.Equal(1, carrot.Wallets);   // one wallet, even though it has two Carrot lines
        Assert.DoesNotContain(mix, r => r.Name == "Carrot Lending");
    }

    [Fact]
    public void Mix_EmptyGroupHasNoRows()
    {
        Assert.Empty(PlatformMix.Build(new List<LeaderboardEntry>()));
        Assert.Empty(PlatformMix.Build(new[] { new LeaderboardEntry { Rank = 1 } }));
    }

    // ---------- the screen ----------

    [Fact]
    public void Screen_ShowsBarPercentAndHowManyWalletsUseEachPlatform()
    {
        var text = MessageFormatter.FormatMix(Sample(), false);

        Assert.Contains("🧭 <b>How the top 3 earn points</b>", text);
        Assert.Matches(@"Kamino\s+█+░*\s+50.0%\s+2/3", text);        // 700 of 1,400
        Assert.Matches(@"Loopscale\s+█+░*\s+14.3%\s+1/3", text);
        Assert.Contains("🥇 Most points: <b>Kamino</b> (50.0% of them)", text);
        Assert.Contains("🤝 Most used: <b>Kamino</b> (2 of 3 wallets)", text);
        Assert.Contains("<blockquote>The bar and percentage", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Screen_RowsFitAPhoneScreen()
    {
        var text = MessageFormatter.FormatMix(Sample(), false);

        var rows = text.Split('\n').Select(l => l.Replace("<pre>", "").Replace("</pre>", "").TrimEnd('\r')).Where(l => l.Contains('█') || l.Contains('░')).ToList();
        Assert.True(rows.Count >= 4);
        Assert.All(rows, r => Assert.True(r.Length <= 42, $"row is {r.Length} characters wide"));
        Assert.Single(rows.Select(r => r.Length).Distinct());   // all rows have the same width, so the columns line up
    }

    [Fact]
    public void Screen_FailureAndEmptyAreExplainedAndCachedDataIsTagged()
    {
        Assert.Contains("currently unavailable", MessageFormatter.FormatMix(null, true));
        Assert.Contains("currently unavailable", MessageFormatter.FormatMix(new LeaderboardPage(), false));

        var page = Sample();
        page.CachedSeconds = 90;
        Assert.Contains("cached data · 1m 30s old", MessageFormatter.FormatMix(page, false));
        Assert.DoesNotContain("cached data", MessageFormatter.FormatMix(Sample(), false));
    }

    [Fact]
    public void Button_MixIsUnderTheLeaderboardAndFitsTheCallbackLimit()
    {
        var button = ReportButtons.MixKeyboard().InlineKeyboard.SelectMany(r => r).Single();

        Assert.Equal(ReportButtons.Mix, button.CallbackData);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(button.CallbackData!) <= 64);
    }
}
