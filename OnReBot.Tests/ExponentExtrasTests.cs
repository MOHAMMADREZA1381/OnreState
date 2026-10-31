using OnReBot.Config;
using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

// Protected (tranche) and Liquidity (CLMM LP) sections. Numbers come from a real API response.
public class ExponentExtrasTests
{
    private static List<ExponentPosition> Parse(string json) => HttpExponentPositionProvider.Parse(json, new()).Positions;

    [Fact]
    public void Tranche_IsShownAsProtectedWithDustHandledGracefully()
    {
        var json = """{"positions":[],"tranches":[{"side":"senior","symbol":"srONyc","lpBalance":0.000020839,"valueUsd":0.000021424,"pnl":-6.9e-10,"apy":0.07792}]}""";

        var p = Assert.Single(Parse(json));

        Assert.Equal("Protected", p.Section);
        Assert.Equal("srONyc", p.Market);
        Assert.Contains("Tranche: Senior (protected)", p.Details);
        Assert.Contains("Balance: 0.000021 srONyc", p.Details);
        Assert.Contains("Value: <$0.01", p.Details);       // tiny values are not rounded to a misleading $0.00
        Assert.Contains("APY: 7.79%", p.Details);
        Assert.Contains("PnL: $0.00", p.Details);          // dust PnL is not shown as -$0.00
    }

    [Fact]
    public void Liquidity_ShowsRangeStatusValueAndEstimatedPnl()
    {
        var json = """{"positions":[],"liquidity":[{"name":"srONyc 10JAN27","lowerApyPct":6,"upperApyPct":10,"inRange":true,"valueUsd":5.8055813,"pnl":-0.000116}]}""";

        var p = Assert.Single(Parse(json));

        Assert.Equal("Liquidity", p.Section);
        Assert.Equal("srONyc 10JAN27", p.Market);
        Assert.Contains("Range: 6.00% – 10.00% (in range)", p.Details);
        Assert.Contains("Value: $5.81", p.Details);
        Assert.Contains("PnL (est.): $0.00", p.Details);
    }

    [Theory]
    [InlineData("false", "(out of range)")]
    [InlineData("null", "")]
    public void Liquidity_RangeStatusVariants(string inRange, string expectedText)
    {
        var json = $$"""{"positions":[],"liquidity":[{"name":"x","lowerApyPct":6,"upperApyPct":10,"inRange":{{inRange}},"valueUsd":1,"pnl":0}]}""";

        var p = Assert.Single(Parse(json));

        Assert.Contains($"Range: 6.00% – 10.00%{(expectedText == "" ? "" : " " + expectedText)}", p.Details);
        if (expectedText == "")
            Assert.DoesNotContain("range)", p.Details);
    }

    [Fact]
    public void RealPnlOnLiquidity_IsSigned()
    {
        var json = """{"positions":[],"liquidity":[{"name":"x","lowerApyPct":6,"upperApyPct":10,"inRange":true,"valueUsd":100,"pnl":-12.5}]}""";

        Assert.Contains("PnL (est.): -$12.50", Assert.Single(Parse(json)).Details);
    }

    [Fact]
    public void FarmPosition_UsesNameFromServiceWhenNotInConfig()
    {
        var json = """{"positions":[{"vault":"y5UF","name":"srONyc 10JAN27","yt":10,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false}]}""";

        var p = Assert.Single(Parse(json));

        Assert.Equal("Farm", p.Section);
        Assert.Equal("srONyc 10JAN27", p.Market);
    }

    [Fact]
    public void FarmPosition_ConfigNameWinsOverServiceName()
    {
        var json = """{"positions":[{"vault":"7f1P","name":"ONyc 10JAN27","yt":10,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false}]}""";
        var markets = new List<ExponentMarketConfig> { new() { Name = "ONYC-JAN2027", Vault = "7f1P" } };

        Assert.Equal("ONYC-JAN2027", Assert.Single(HttpExponentPositionProvider.Parse(json, markets).Positions).Market);
    }

    [Fact]
    public void ExtrasFailedFlagAndCacheAgeArePassedThrough()
    {
        var json = """{"positions":[],"tranches":[{"side":"junior","symbol":"jrONyc","lpBalance":1,"valueUsd":1,"pnl":0,"apy":null}],"extrasFailed":true,"extrasCachedAgeSeconds":42}""";

        var result = HttpExponentPositionProvider.Parse(json, new());

        Assert.True(result.ExtrasFailed);
        var p = Assert.Single(result.Positions);
        Assert.Equal(42, p.CachedSeconds);
        Assert.Contains("Tranche: Junior", p.Details);
        Assert.DoesNotContain("APY:", p.Details);
    }

    [Fact]
    public void Formatter_ShowsSectionInTheHeader()
    {
        var report = new PortfolioReport { Exponent = { new ExponentPosition { Section = "Protected", Market = "srONyc", Details = "Value: $1.00" } } };

        Assert.Contains("🔹 <b>Exponent Protected</b> · srONyc", MessageFormatter.Format(report));
    }

    [Fact]
    public void Formatter_WarnsWhenProtectedOrLiquidityCouldNotBeLoaded()
    {
        var report = new PortfolioReport
        {
            OnRe = new OnReResponse { Rank = 5, TotalPoints = 100 },
            ExponentExtrasFailed = true
        };

        var text = MessageFormatter.Format(report);

        Assert.Contains("Exponent Protected / Liquidity", text);
        Assert.Contains("currently unavailable", text);
        Assert.DoesNotContain("No active positions found.", text);   // the list may be incomplete, so do not claim there are none
    }
}
