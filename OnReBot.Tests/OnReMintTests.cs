using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Tests;

public class OnReMintTests
{
    private static ExponentOverview Overview(decimal? onycNav = 1.162146115m) => new()
    {
        Markets =
        {
            new MarketInfo { Name = "srONyc 10JAN27", TokenPrice = 1.0287m },
            new MarketInfo { Name = "ONyc 10JAN27", TokenPrice = onycNav }
        },
        CachedAgeSeconds = 30
    };

    [Fact]
    public void Nav_IsTheOnycMarketRateNotTheSrOnycOne()
    {
        Assert.Equal(1.162146115m, OnReMint.Nav(Overview()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void Nav_MissingOrZeroIsUnknown(double? value)
    {
        Assert.Null(OnReMint.Nav(Overview(value == null ? null : (decimal)value)));
        Assert.Null(OnReMint.Nav(null));
        Assert.Null(OnReMint.Nav(new ExponentOverview()));
    }

    [Fact]
    public void Rate_MintsAtNavWithNoFeeAndNoImpact()
    {
        var rate = OnReMint.Rate(100m, 1.162146115m, 30);

        Assert.True(rate.Buy);
        Assert.Equal(OnReMint.Label, rate.Dex);
        Assert.Equal(86.0477m, Math.Round(rate.AmountOut, 4));   // 100 / 1.162146115
        Assert.Equal(1.162146115m, rate.UsdPerOnyc);
        Assert.Equal(0m, rate.PriceImpactPct);
        Assert.Equal(30, rate.CachedSeconds);
    }

    [Fact]
    public void SwapMessage_ListsTheMintAmongTheDexesAndExplainsRedemption()
    {
        var rates = new SwapRates
        {
            Rates =
            {
                new SwapRate { Buy = true, Dex = "Whirlpool", AmountIn = 100m, AmountOut = 86.4696m, UsdPerOnyc = 1.1565m },
                OnReMint.Rate(100m, 1.162146115m, null)
            }
        };

        var text = MessageFormatter.FormatSwapRates(rates);

        Assert.Contains("✅ <b>Orca</b>: $1.1565", text);   // the DEX is cheaper than minting at NAV
        Assert.Contains("▫️ <b>OnRe mint</b>: $1.1621", text);
        Assert.Contains("OnRe mint: ONyc at OnRe's own price (NAV), no mint fee", text);
        Assert.Contains("Selling to OnRe is not an instant swap", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Calculator_ComparesTheMintWithTheDexesWhenBuying()
    {
        var rates = new List<SwapRate>
        {
            new() { Buy = true, Dex = "Whirlpool", AmountIn = 100m, AmountOut = 86.4696m, UsdPerOnyc = 1.1565m },
            OnReMint.Rate(100m, 1.162146115m, null)
        };

        var text = MessageFormatter.FormatCalculator(true, 100m, rates);

        Assert.Contains("✅ <b>Orca</b>: 86.4696 ONyc", text);
        Assert.Contains("▫️ <b>OnRe mint</b>: 86.0477 ONyc", text);
        Assert.Contains("ONyc at OnRe's own price", text);
        Assert.DoesNotContain("Selling to OnRe", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Calculator_SellingExplainsWhyRedemptionIsNotListed()
    {
        var rates = new List<SwapRate> { new() { Buy = false, Dex = "Whirlpool", AmountIn = 50m, AmountOut = 57.8123m, UsdPerOnyc = 1.1562m } };

        var text = MessageFormatter.FormatCalculator(false, 50m, rates);

        Assert.Contains("Selling to OnRe is not an instant swap", text);
        Assert.DoesNotContain("OnRe mint", text);
        Assert.Null(TelegramHtml.Validate(text));
    }
}
