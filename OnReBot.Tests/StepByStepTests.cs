using System.Text;
using OnReBot.Models;

namespace OnReBot.Tests;

// The step-by-step flow: quick summary first, then a button for the full report.
public class StepByStepTests
{
    private const string Wallet = "TestWa11etAddressForUnitTestsJust1111";
    private static readonly string LongestWallet = new('1', 44);

    private static OnReResponse Onre() => new() { Rank = 1427, TotalPoints = 22543333, Address = Wallet };

    // ---------- buttons ----------

    [Fact]
    public void FullButtonData_RoundTripsTheWallet()
    {
        var data = ReportButtons.FullData(Wallet);

        Assert.True(ReportButtons.TryParseFull(data, out var parsed));
        Assert.Equal(Wallet, parsed);
    }

    [Fact]
    public void FullButtonData_FitsTelegramsLimitEvenForTheLongestAddress()
    {
        var data = ReportButtons.FullData(LongestWallet);

        Assert.True(Encoding.UTF8.GetByteCount(data) <= 64);   // Telegram allows 64 bytes of callback data
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rank")]
    [InlineData("no")]
    [InlineData("full:")]
    [InlineData("full:abc")]
    [InlineData("full:0estWa11etAddressForUnitTestsJust1111")]   // contains a character that is not base58
    [InlineData("FULL:TestWa11etAddressForUnitTestsJust1111")]   // prefix is case-sensitive
    public void FullButtonData_RejectsAnythingElse(string? data)
    {
        Assert.False(ReportButtons.TryParseFull(data, out var wallet));
        Assert.Equal("", wallet);
    }

    [Fact]
    public void Keyboard_HasFullReportAndDeclineButtons()
    {
        var buttons = ReportButtons.Keyboard(Wallet).InlineKeyboard.SelectMany(row => row).ToList();

        Assert.Equal(2, buttons.Count);
        Assert.Equal("✅ Full report", buttons[0].Text);
        Assert.Equal(ReportButtons.FullData(Wallet), buttons[0].CallbackData);
        Assert.Equal("❌ No thanks", buttons[1].Text);
        Assert.Equal(ReportButtons.Decline, buttons[1].CallbackData);
    }

    // ---------- step 1: summary ----------

    [Fact]
    public void Summary_ShowsRankAndPointsThenAsksAboutTheFullReport()
    {
        var text = MessageFormatter.FormatSummary(Onre(), onReFailed: false);

        Assert.Contains("Rank: <b>#1,427</b>", text);
        Assert.Contains("Total points: <b>22,543,333</b>", text);
        Assert.Contains("Want your <b>positions</b> too", text);
        Assert.DoesNotContain("Active positions", text);   // positions come only after the user asks
    }

    [Fact]
    public void Summary_ExplainsThatDataMayBeTemporarilyMissing()
    {
        var text = MessageFormatter.FormatSummary(Onre(), onReFailed: false);

        Assert.Contains("<i>(All requests are sent from a single IP address, so some data may be temporarily unavailable.)</i>", text);
    }

    [Fact]
    public void Summary_StillAsksWhenOnReIsUnavailable()
    {
        var text = MessageFormatter.FormatSummary(null, onReFailed: true);

        Assert.Contains("<b>OnRe points</b>: ⚠️ <i>currently unavailable</i>", text);
        Assert.Contains("Want your <b>positions</b> too", text);
    }

    [Fact]
    public void Summary_ForAWalletWithoutPoints_DoesNotShowAMeaninglessRank()
    {
        var text = MessageFormatter.FormatSummary(new OnReResponse { Rank = 24948, TotalPoints = 0 }, onReFailed: false);

        Assert.Contains("<b>OnRe points</b>: <i>none yet</i>", text);
        Assert.DoesNotContain("24,948", text);
        Assert.Contains("Want your <b>positions</b> too", text);   // it may still have positions
    }

    // ---------- step 2: positions ----------

    [Fact]
    public void Positions_HaveNoRankBlockBecauseStepOneAlreadyShowedIt()
    {
        var report = new PortfolioReport
        {
            OnRe = Onre(),
            Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 5.98m } }
        };

        var text = MessageFormatter.FormatPositions(report);

        Assert.StartsWith("💼 <b>Active positions</b>", text);
        Assert.Contains("<b>Loopscale</b> · USDC OnRe", text);
        Assert.DoesNotContain("Rank:", text);
        Assert.DoesNotContain("OnRe Rank Report", text);
    }

    [Fact]
    public void Positions_ForAnEmptyWallet_SayNoActivity()
    {
        var report = new PortfolioReport { OnRe = new OnReResponse { Rank = 24948, TotalPoints = 0 } };

        Assert.Contains("<b>No activity found</b> for this wallet.", MessageFormatter.FormatPositions(report));
    }

    [Fact]
    public void Positions_WhenAPlatformFailed_StillShowTheOthers()
    {
        var report = new PortfolioReport
        {
            OnRe = Onre(),
            KaminoFailed = true,
            Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 1 } }
        };

        var text = MessageFormatter.FormatPositions(report);

        Assert.Contains("<b>Kamino</b>", text);
        Assert.Contains("currently unavailable", text);
        Assert.Contains("<b>Loopscale</b> · USDC OnRe", text);
    }

    [Fact]
    public void WholeReport_IsStillAvailableAsOneMessage()
    {
        var report = new PortfolioReport
        {
            OnRe = Onre(),
            Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 1 } }
        };

        var text = MessageFormatter.Format(report);

        Assert.Contains("Rank: <b>#1,427</b>", text);
        Assert.Contains("Active positions", text);
    }
}
