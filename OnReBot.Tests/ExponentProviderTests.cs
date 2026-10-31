using OnReBot.Config;
using OnReBot.Services;

namespace OnReBot.Tests;

public class ExponentProviderTests
{
    [Fact]
    public void ParsesServiceResponseAndMapsVaultToMarketName()
    {
        var json = """{"positions":[{"vault":"7f1P","yt":41372.69943375,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false}]}""";
        var markets = new List<ExponentMarketConfig> { new() { Name = "ONYC-JAN2027", Vault = "7f1P" } };

        var p = Assert.Single(HttpExponentPositionProvider.Parse(json, markets).Positions);

        Assert.Equal("ONYC-JAN2027", p.Market);
        Assert.Equal("YT balance: 41,372.70\nMaturity: 2027-01-10\nClaimable interest: 0.00\nPnL: temporarily unavailable", p.Details);
    }

    [Fact]
    public void ShowsCostBasisAndPnlWhenApiDataPresent()
    {
        // اعداد از اسکرین‌شات اپ Exponent برای ولت تست: -154.14 + 203.18 - 75.28 = -26.24
        var json = """{"positions":[{"vault":"7f1P","yt":41372.7,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false,"avgCostBasis":0.029177,"currentValue":984.90,"priceAppreciation":-154.14,"claimedInterestUsd":203.18,"realizedPnl":-75.28,"unclaimedYieldUsd":0,"totalPnl":-26.24}]}""";

        var p = Assert.Single(HttpExponentPositionProvider.Parse(json, new()).Positions);

        Assert.Contains("\nCurrent value: $984.90", p.Details);
        Assert.Contains("\nAvg cost per YT: $0.0292", p.Details);
        Assert.Contains("\nTotal PnL: -$26.24", p.Details);
        Assert.Contains("\n  ◦ Price appreciation: -$154.14", p.Details);
        Assert.Contains("\n  ◦ Claimed yield: +$203.18", p.Details);
        Assert.Contains("\n  ◦ Realized PnL: -$75.28", p.Details);
        Assert.DoesNotContain("Unclaimed yield", p.Details);   // صفر است و نمایش داده نمی‌شود
    }

    [Fact]
    public void NoPnlLinesWhenApiDataMissing()
    {
        var json = """{"positions":[{"vault":"7f1P","yt":10,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false}]}""";

        var p = Assert.Single(HttpExponentPositionProvider.Parse(json, new()).Positions);

        Assert.DoesNotContain("Total PnL", p.Details);
        Assert.Contains("\nPnL: temporarily unavailable", p.Details);
    }

    [Fact]
    public void EmptyResponseMeansNoPositions() =>
        Assert.Empty(HttpExponentPositionProvider.Parse("""{"positions":[]}""", new()).Positions);
}
