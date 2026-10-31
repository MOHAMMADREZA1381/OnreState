using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

public class CachedTagTests
{
    [Fact]
    public void FreshData_HasNoCachedTag()
    {
        var report = new PortfolioReport { Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 5 } } };

        Assert.DoesNotContain("cached", MessageFormatter.Format(report));
    }

    [Fact]
    public void CachedLoopscale_ShowsTagWithAge()
    {
        var report = new PortfolioReport { Loopscale = { new LoopscalePosition { VaultName = "USDC OnRe", Usdc = 5, CachedSeconds = 12 } } };

        Assert.Contains("🕒 <i>cached data · 12s old</i>", MessageFormatter.Format(report));
    }

    [Fact]
    public void CachedExponent_ShowsTagWithMinutes()
    {
        var report = new PortfolioReport { Exponent = { new ExponentPosition { Market = "ONYC-JAN2027", Details = "YT balance: 1.00", CachedSeconds = 135 } } };

        Assert.Contains("🕒 <i>cached data · 2m 15s old</i>", MessageFormatter.Format(report));
    }

    [Fact]
    public void ProviderPassesCachedAgeFromService()
    {
        var json = """{"positions":[{"vault":"7f1P","yt":10,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false,"cachedAgeSeconds":42}]}""";

        var p = Assert.Single(HttpExponentPositionProvider.Parse(json, new()).Positions);

        Assert.Equal(42, p.CachedSeconds);
    }

    [Fact]
    public void ProviderLeavesCachedNullWhenFresh()
    {
        var json = """{"positions":[{"vault":"7f1P","yt":10,"claimableInterest":0,"expiresAt":"2027-01-10T13:00:00.000Z","expired":false,"cachedAgeSeconds":null}]}""";

        var p = Assert.Single(HttpExponentPositionProvider.Parse(json, new()).Positions);

        Assert.Null(p.CachedSeconds);
    }
}
