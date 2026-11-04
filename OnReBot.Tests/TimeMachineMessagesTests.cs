using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

public class TimeMachineMessagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static MomentumResult Ok(bool week = false) => new()
    {
        Status = MomentumStatus.Ok,
        Day = new Momentum { Window = TimeSpan.FromHours(24), OldRank = 1354, NewRank = 1345, OldPoints = 24_000_000m, NewPoints = 24_012_400m },
        Week = week ? new Momentum { Window = TimeSpan.FromDays(7), OldRank = 1500, NewRank = 1345, OldPoints = 23_900_000m, NewPoints = 24_012_400m } : null,
        Goals = { (1000, 34.2m), (500, 400m) },
        SnapshotAge = TimeSpan.FromMinutes(14)
    };

    // ---------- my momentum ----------

    [Fact]
    public void Momentum_ShowsPointsPerDayRankChangeAndTheGoals()
    {
        var text = MessageFormatter.FormatMomentum(Ok());

        Assert.Contains("📈 <b>Your momentum</b>", text);
        Assert.Contains("🗓 <b>Last 24h</b>", text);
        Assert.Matches(@"Points\s+\+12,400\s+\(12,400/day\)", text);
        Assert.Matches(@"Rank\s+↑9\s+#1,354 → #1,345", text);
        Assert.Contains("🎯 <b>At the pace of the last 24h</b>", text);
        Assert.Contains("• #1,000: in ~35 days", text);       // 34.2 days, rounded up
        Assert.Contains("• #500: in over a year", text);
        Assert.Contains("📸 <i>snapshot from 14m 0s ago</i>", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Momentum_AddsTheWeekWhenThereIsOne()
    {
        var text = MessageFormatter.FormatMomentum(Ok(week: true));

        Assert.Contains("🗓 <b>Last 7 days</b>", text);
        Assert.Matches(@"Points\s+\+112,400\s+\(16,057/day\)", text);
    }

    [Fact]
    public void Momentum_AWalletThatFellOrStoodStillIsShownHonestly()
    {
        var result = Ok();
        result.Day!.NewPoints = result.Day.OldPoints;       // no points gained
        result.Day.NewRank = 1360;                            // and pushed down by others

        var text = MessageFormatter.FormatMomentum(result);

        Assert.Matches(@"Rank\s+↓6\s+#1,354 → #1,360", text);
        Assert.Contains("Your points did not change in this window.", text);
        Assert.DoesNotContain("🎯", text);   // no pace, no projection
    }

    [Fact]
    public void Momentum_ANewWalletSaysItIsNew()
    {
        var result = Ok();
        result.Day!.IsNew = true;
        result.Day.OldPoints = 0;
        result.Day.OldRank = 25_080;

        Assert.Contains("new on the leaderboard → #1,345", MessageFormatter.FormatMomentum(result));
    }

    [Fact]
    public void Momentum_ShortWindowsAreNamedByTheirRealLength()
    {
        var result = Ok();
        result.Day!.Window = TimeSpan.FromHours(9.5);

        Assert.Contains("🗓 <b>Last 9h</b>", MessageFormatter.FormatMomentum(result));
    }

    [Theory]
    [InlineData(0.3, "in less than a day")]
    [InlineData(1, "in ~1 days")]
    [InlineData(364.1, "in ~365 days")]
    [InlineData(366, "in over a year")]
    public void Momentum_DaysAreRoundedUpAndHugeOnesAreSaidInWords(double days, string expected)
    {
        var result = Ok();
        result.Goals = new() { (1000, (decimal)days) };

        Assert.Contains(expected, MessageFormatter.FormatMomentum(result));
    }

    [Fact]
    public void Momentum_WithoutDataOrOutsideTheLeaderboardExplainsWhy()
    {
        var waiting = MessageFormatter.FormatMomentum(new MomentumResult { Status = MomentumStatus.NotEnoughData, WaitFor = TimeSpan.FromHours(4) });
        Assert.Contains("Collecting data", waiting);
        Assert.Contains("ready in about 4 hours", waiting);
        Assert.Contains("ready in about 20 minutes", MessageFormatter.FormatMomentum(new MomentumResult { Status = MomentumStatus.NotEnoughData, WaitFor = TimeSpan.FromMinutes(20) }));
        Assert.Null(TelegramHtml.Validate(waiting));

        var outside = MessageFormatter.FormatMomentum(new MomentumResult { Status = MomentumStatus.NotOnLeaderboard });
        Assert.Contains("not on the OnRe leaderboard", outside);
        Assert.Null(TelegramHtml.Validate(outside));
    }

    // ---------- movers ----------

    private static MoversResult Movers() => new()
    {
        Window = TimeSpan.FromHours(24),
        SnapshotAge = TimeSpan.FromMinutes(3),
        WalletsNow = 25_079,
        WalletsBefore = 24_994,
        Gainers =
        {
            new Mover("DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh", 1, 1, 123_456_789m, false),
            new Mover("NewWallet11111111111111111111111111111111aaaa", 25_000, 900, 5_000m, true)
        },
        Climbers = { new Mover("AbCdEfGhIjKlMnOpQrStUvWxYz1234567890abcdWXYZ", 2_340, 1_901, 100m, false) },
        NewInTop100 =
        {
            new Mover("TopTopTopTopTopTopTopTopTopTopTopTopTopTop1111", 140, 57, 9m, false),
            new Mover("NewTop111111111111111111111111111111111111zzzz", 25_080, 99, 9m, true)
        }
    };

    [Fact]
    public void Movers_ShowsGainersClimbersAndNewTop100()
    {
        var text = MessageFormatter.FormatMovers(Movers());

        Assert.Contains("🚀 <b>Movers</b> · last 24h", text);
        Assert.Matches(@"1 DTjNdf…Snvh\s+\+123,456,789", text);
        Assert.Matches(@"2 NewWal…aaaa\s+\+5,000 new", text);
        Assert.Matches(@"1 AbCdEf…WXYZ\s+#2,340 → #1,901 ↑439", text);
        Assert.Contains("#57  TopTop…1111  from #140", text);
        Assert.Contains("#99  NewTop…zzzz  new wallet", text);
        Assert.Contains("25,079 wallets with points (+85)", text);
        Assert.Contains("📸 <i>snapshot from 3m 0s ago</i>", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Movers_RowsFitAPhoneScreen()
    {
        var text = MessageFormatter.FormatMovers(Movers());

        var rows = text.Split('\n').Select(l => l.Replace("<pre>", "").Replace("</pre>", "").TrimEnd('\r')).Where(l => l.Contains('…')).ToList();
        Assert.True(rows.Count >= 5);
        Assert.All(rows, r => Assert.True(r.Length <= 42, $"row is {r.Length} characters wide: {r}"));
    }

    [Fact]
    public void Movers_NothingNewAndNotEnoughDataAreSaid()
    {
        var quiet = Movers();
        quiet.NewInTop100.Clear();
        Assert.Contains("nobody new", MessageFormatter.FormatMovers(quiet));

        var waiting = MessageFormatter.FormatMovers(new MoversResult { NotEnoughData = true });
        Assert.Contains("Collecting data", waiting);
        Assert.Null(TelegramHtml.Validate(waiting));
    }

    // ---------- reading pages and buttons ----------

    [Fact]
    public void SnapshotPage_IsReadFromTheRealResponseShape()
    {
        const string json = """
        {"leaderboard":{"content":[{"rank":1,"address":"DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh","totalPoints":19669966829,"pointsBreakdown":{"wallet":3}},
                                    {"rank":2,"address":"E3NiM5","totalPoints":15287032683,"pointsBreakdown":{}}],"totalPages":13,"totalElements":25079}}
        """;

        var (entries, totalPages) = SnapshotJob.ParsePage(json);

        Assert.Equal(13, totalPages);
        Assert.Equal(new SnapshotEntry(1, "DTjNdfYmKpwGjnX6UPGvxwXQ7PZEGM7M43BQLtyASnvh", 19_669_966_829m), entries[0]);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Buttons_MomentumAndMoversExistAndFitTheCallbackLimit()
    {
        var wallet = new string('A', 44);
        var data = ReportButtons.MomentumData(wallet);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(ReportButtons.TryParseMomentum(data, out var parsed));
        Assert.Equal(wallet, parsed);
        Assert.False(ReportButtons.TryParseMomentum("mom:nope", out _));

        var summary = ReportButtons.Keyboard("TestWa11etAddressForUnitTestsJust1111").InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Contains(summary, b => b.CallbackData == ReportButtons.MomentumData("TestWa11etAddressForUnitTestsJust1111"));

        var board = ReportButtons.BoardFinalKeyboard().InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Equal(new[] { ReportButtons.Mix, ReportButtons.Movers }, board.Select(b => b.CallbackData).ToArray());
        Assert.All(board, b => Assert.True(System.Text.Encoding.UTF8.GetByteCount(b.CallbackData!) <= 64));
    }
}
