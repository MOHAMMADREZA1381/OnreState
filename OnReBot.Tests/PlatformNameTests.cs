using OnReBot.Models;

namespace OnReBot.Tests;

public class PlatformNameTests
{
    private static SwapRates Rates(params SwapRate[] rates) => new() { Rates = rates.ToList() };

    private static SwapRate Dex(string dex) => new() { Buy = true, Dex = dex, AmountIn = 100m, AmountOut = 86m, UsdPerOnyc = 1.16m };

    [Theory]
    [InlineData("Whirlpool", "Orca")]
    [InlineData("Orca V2", "Orca")]
    [InlineData("Raydium CLMM", "Raydium")]
    [InlineData("Raydium CP", "Raydium")]
    [InlineData("Raydium", "Raydium")]
    [InlineData("Meteora DLMM", "Meteora")]
    public void PoolTypesAreShownAsThePlatformName(string label, string platform)
    {
        var text = MessageFormatter.FormatSwapRates(Rates(Dex(label)));

        Assert.Contains($"<b>{platform}</b>: $1.1600", text);
        if (label != platform)
            Assert.DoesNotContain($"<b>{label}</b>", text);
    }

    [Fact]
    public void UnknownLabelsAreShownAsTheyCome()
    {
        var text = MessageFormatter.FormatSwapRates(Rates(Dex("AlphaQ")));

        Assert.Contains("<b>AlphaQ</b>: $1.1600", text);
    }

    [Fact]
    public void RoutesNamePlatformsOnceEach()
    {
        var route = new SwapRate
        {
            Buy = true, Source = "Titan", AmountIn = 100m, AmountOut = 86m, UsdPerOnyc = 1.16m,
            Routes = ["Whirlpool", "Orca V2", "Raydium CLMM", "AlphaQ"]
        };

        var text = MessageFormatter.FormatSwapRates(Rates(Dex("Whirlpool"), route));

        Assert.Contains("<i>· via Orca, Raydium +1</i>", text);   // the two Orca pools count once; AlphaQ is the "+1"
    }
}
