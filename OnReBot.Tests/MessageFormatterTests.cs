using OnReBot.Models;

namespace OnReBot.Tests;

public class MessageFormatterTests
{
    private static PortfolioReport Sample()
    {
        var onRe = new OnReResponse { Rank = 1427, TotalPoints = 22543333, Address = "TestWa11etAddressForUnitTestsJust1111" };
        onRe.PointsBreakdown.Wallet = 3021;
        onRe.PointsBreakdown.Exponent.Yt = 20342393;
        onRe.PointsBreakdown.Exponent.Markets["ONYC-JAN2027"] = new() { Yt = 6838325 };
        onRe.PointsBreakdown.Exponent.Markets["SEP-2026"] = new() { Yt = 0 };

        return new PortfolioReport
        {
            OnRe = onRe,
            Kamino = { new KaminoPosition { MarketName = "OnRe Market", Tokens = { new() { Symbol = "ONyc", Amount = 10m } }, TotalDepositUsd = 11.6048304m } },
            Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 5.984968m } }
        };
    }

    [Fact]
    public void FormatsFullReport()
    {
        var text = MessageFormatter.Format(Sample());

        Assert.Contains("Rank: <b>#1,427</b>", text);
        Assert.Contains("Total points: <b>22,543,333</b>", text);
        Assert.Contains("• Exponent: <b>20,342,393</b>", text);
        Assert.Contains("◦ ONYC-JAN2027: 6,838,325", text);
        Assert.DoesNotContain("• Wallet:", text);         // wallet-balance points are intentionally not listed
        Assert.DoesNotContain("SEP-2026", text);          // zero markets are hidden
        Assert.DoesNotContain("Orca", text);              // zero platforms are hidden
        Assert.Contains("<b>Kamino</b> · OnRe Market", text);
        Assert.Contains("Deposit: 10.00 ONyc (≈ $11.60)", text);
        Assert.Contains("Debt: none", text);
        Assert.Contains("<b>Loopscale</b> · USDC OnRe", text);
        Assert.Contains("Deposit: 5.98 USDC", text);
    }

    [Fact]
    public void FailedPlatformDoesNotBreakOtherSections()
    {
        var report = Sample();
        report.KaminoFailed = true;
        report.Kamino.Clear();

        var text = MessageFormatter.Format(report);

        Assert.Contains("<b>Kamino</b>\n   <i>currently unavailable</i>", text.Replace("\r\n", "\n"));
        Assert.Contains("Rank: <b>#1,427</b>", text);
        Assert.Contains("Loopscale</b> · USDC OnRe", text);
    }

    [Fact]
    public void NoPositions_SaysSo()
    {
        var report = new PortfolioReport { OnRe = new OnReResponse { Rank = 5, TotalPoints = 100 } };
        Assert.Contains("No active positions found.", MessageFormatter.Format(report));
    }

    [Fact]
    public void DynamicTextIsHtmlEscaped()
    {
        var report = new PortfolioReport { Loopscale = { new LoopscalePosition { VaultName = "<b>x</b>", Usdc = 1 } } };
        var text = MessageFormatter.Format(report);

        Assert.DoesNotContain("<b>x</b>", text);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", text);
    }

    [Fact]
    public void UnknownWallet_ShowsNoActivityInsteadOfRank()
    {
        // OnRe answers with a rank and 0 points even for addresses it has never seen
        var report = new PortfolioReport { OnRe = new OnReResponse { Rank = 24948, TotalPoints = 0 } };

        var text = MessageFormatter.Format(report);

        Assert.Contains("No activity found for this wallet.", text);
        Assert.DoesNotContain("24,948", text);
        Assert.DoesNotContain("Rank", text);
    }

    [Fact]
    public void ZeroPointsButPositions_ShowsPositionsWithoutRank()
    {
        var report = new PortfolioReport
        {
            OnRe = new OnReResponse { Rank = 24948, TotalPoints = 0 },
            Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 2 } }
        };

        var text = MessageFormatter.Format(report);

        Assert.Contains("OnRe points: <i>none yet</i>", text);
        Assert.Contains("Deposit: 2.00 USDC", text);
        Assert.DoesNotContain("#24,948", text);
    }
}
