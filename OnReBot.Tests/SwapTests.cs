using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Tests;

public class SwapTests
{
    // Shape of Jupiter's quote response (values from real calls)
    private const string BuyJson = """
    {"inputMint":"EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v","inAmount":"100000000","outputMint":"5Y8NV33Vv7WbnLfq3zBcKSdYPrk7g2KoiQoe7M2tcxp5",
     "outAmount":"86469592037","priceImpactPct":"0.000022514066066709305362908",
     "routePlan":[{"swapInfo":{"label":"Whirlpool","outAmount":"86469592037"},"percent":100}]}
    """;

    private const string SellJson = """
    {"outAmount":"115626459","priceImpactPct":"0.000160215699749441477624021",
     "routePlan":[{"swapInfo":{"label":"Raydium CLMM"},"percent":60},{"swapInfo":{"label":"Whirlpool"},"percent":30},{"swapInfo":{"label":"Raydium CLMM"},"percent":10}]}
    """;

    private static SwapRate Quote(bool buy, string? dex, decimal usdPerOnyc, int? cached = null) =>
        new() { Buy = buy, Dex = dex, Source = dex == null ? "Jupiter" : null, AmountIn = 100m, AmountOut = 100m, UsdPerOnyc = usdPerOnyc, Routes = dex == null ? ["Raydium CLMM", "Hadron"] : [dex], CachedSeconds = cached };

    private static SwapRates Rates() => new()
    {
        Rates =
        {
            Quote(true, "Raydium CLMM", 1.1568m),
            Quote(true, "Whirlpool", 1.1565m),
            Quote(true, null, 1.1564m),
            Quote(false, "Raydium CLMM", 1.1560m),
            Quote(false, "Whirlpool", 1.1562m),
            Quote(false, null, 1.1563m)
        }
    };

    [Fact]
    public void Quote_BuyGivesOnycAndUsdPerOnyc()
    {
        var rate = JupiterClient.ParseQuote(BuyJson, buy: true, 100m, 9, "Whirlpool");

        Assert.Equal(86.469592037m, rate.AmountOut);
        Assert.Equal(1.1565m, Math.Round(rate.UsdPerOnyc, 4));   // 100 USDC / 86.47 ONyc
        Assert.Equal("Whirlpool", rate.Dex);
        Assert.Equal(["Whirlpool"], rate.Routes);
    }

    [Fact]
    public void Quote_SellGivesUsdcAndListsEachDexOnce()
    {
        var rate = JupiterClient.ParseQuote(SellJson, buy: false, 100m, 6);

        Assert.Equal(115.626459m, rate.AmountOut);
        Assert.Equal(1.1563m, Math.Round(rate.UsdPerOnyc, 4));   // 115.63 USDC / 100 ONyc
        Assert.Null(rate.Dex);
        Assert.Equal(["Raydium CLMM", "Whirlpool"], rate.Routes);
    }

    [Fact]
    public void Quote_WithoutOutputIsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => JupiterClient.ParseQuote("""{"outAmount":"0","routePlan":[]}""", true, 100m, 9));
    }

    [Fact]
    public void Message_SaysInPlainWordsWhatOneOnycCostsOnEachDex()
    {
        var text = MessageFormatter.FormatSwapRates(Rates());

        Assert.Contains("💱 <b>ONyc price on DEXes</b>", text);
        Assert.Contains("<b>Buying 1 ONyc costs</b>", text);
        Assert.Contains("<b>Selling 1 ONyc gives</b>", text);
        Assert.Contains("<b>Orca</b>: $1.1565", text);
        Assert.Contains("<b>Raydium</b>: $1.1568", text);
    }

    [Fact]
    public void Message_BestPriceComesFirstWithACheckMark()
    {
        var text = MessageFormatter.FormatSwapRates(Rates());

        // buying: the DEXes cheapest first (Whirlpool 1.1565, then Raydium CLMM 1.1568); only the best price overall gets the ✅ (Jupiter 1.1564)
        var buyWhirlpool = text.IndexOf("▫️ <b>Orca</b>: $1.1565", StringComparison.Ordinal);
        var buyRaydium = text.IndexOf("▫️ <b>Raydium</b>: $1.1568", StringComparison.Ordinal);
        var buyBest = text.IndexOf("✅ <b>Jupiter</b>: $1.1564", StringComparison.Ordinal);
        Assert.True(buyWhirlpool >= 0 && buyWhirlpool < buyRaydium && buyRaydium < buyBest);

        // selling: the highest first; Jupiter 1.1563 is the best there
        Assert.True(text.IndexOf("✅ <b>Jupiter</b>: $1.1563", StringComparison.Ordinal) > buyBest);
    }

    [Fact]
    public void Message_ShowsJupitersBestRouteWithItsDexes()
    {
        var text = MessageFormatter.FormatSwapRates(Rates());

        Assert.Contains("<b>Jupiter</b>: $1.1564 <i>· via Raydium, Hadron</i>", text);
    }

    [Fact]
    public void Titan_QuoteUsesItsRecommendedProviderAndListsDexesOnce()
    {
        const string json = """
        {"id":"swap:x","quotes":{"Titan":{"inAmount":100000000,"outAmount":86469915556,"steps":[{"label":"Whirlpool"},{"label":"Whirlpool"},{"label":"Raydium CLMM"}]},
                                  "Other":{"inAmount":100000000,"outAmount":99999999999,"steps":[{"label":"X"}]}},
         "metadata":{"ExpectedWinner":"Titan"}}
        """;

        var rate = TitanClient.ParseQuote(json, buy: true, 100m, 9);

        Assert.Equal("Titan", rate.Source);
        Assert.Equal(86.469915556m, rate.AmountOut);
        Assert.Equal(1.1565m, Math.Round(rate.UsdPerOnyc, 4));
        Assert.Equal(["Whirlpool", "Raydium CLMM"], rate.Routes);
    }

    [Fact]
    public void Titan_QuoteWithoutWinnerTakesTheLargestOutput()
    {
        const string json = """{"quotes":{"A":{"outAmount":115000000,"steps":[]},"B":{"outAmount":115626099,"steps":[{"label":"AlphaQ"}]}}}""";

        var rate = TitanClient.ParseQuote(json, buy: false, 100m, 6);

        Assert.Equal(115.626099m, rate.AmountOut);
        Assert.Equal(["AlphaQ"], rate.Routes);
    }

    [Fact]
    public void Titan_NoQuotesIsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => TitanClient.ParseQuote("""{"quotes":{}}""", true, 100m, 9));
    }

    [Fact]
    public async Task Titan_WithoutAnApiKeyReturnsNothing()
    {
        var titan = new TitanClient(new HttpClient(), null);

        Assert.False(titan.Enabled);
        Assert.Empty(await titan.GetRatesAsync(CancellationToken.None));
    }

    [Fact]
    public void Message_ListsTitanNextToJupiterAndNamesBothSources()
    {
        var rates = Rates();
        rates.Rates.Add(new SwapRate { Buy = true, Source = "Titan", AmountIn = 100m, AmountOut = 100m, UsdPerOnyc = 1.1563m, Routes = ["Whirlpool"] });

        var text = MessageFormatter.FormatSwapRates(rates);

        Assert.Contains("✅ <b>Titan</b>: $1.1563 <i>· via Orca</i>", text);   // cheapest for buying
        Assert.Contains("Jupiter + Titan", text);
        Assert.Null(TelegramHtml.Validate(text));
        Assert.Contains("· Jupiter ·", MessageFormatter.FormatSwapRates(Rates()));
    }

    [Fact]
    public void Message_TagsCachedQuotesAndIsValidTelegramHtml()
    {
        var rates = Rates();
        rates.Rates[0] = rates.Rates[0] with { CachedSeconds = 12 };

        var text = MessageFormatter.FormatSwapRates(rates);

        Assert.Contains("cached data · 12s old", text);
        Assert.Null(TelegramHtml.Validate(text));
        Assert.DoesNotContain("cached data", MessageFormatter.FormatSwapRates(Rates()));
    }

    [Fact]
    public void Message_FailureSaysUnavailable()
    {
        var text = MessageFormatter.FormatSwapRates(new SwapRates { Failed = true });

        Assert.Contains("currently unavailable", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void SwapButton_IsOnTheStartKeyboardAndFitsTheCallbackLimit()
    {
        var buttons = ReportButtons.StartKeyboard().InlineKeyboard.SelectMany(r => r).ToList();

        Assert.Contains(buttons, b => b.CallbackData == ReportButtons.Swap);
        Assert.All(buttons, b => Assert.True(System.Text.Encoding.UTF8.GetByteCount(b.CallbackData!) <= 64));
    }
}
