using OnReBot.Models;

namespace OnReBot.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("100", 100)]
    [InlineData("  100  ", 100)]
    [InlineData("$100", 100)]
    [InlineData("1,000.50", 1000.50)]
    [InlineData("100 usd", 100)]
    [InlineData("100USDC", 100)]
    [InlineData("2.5 onyc", 2.5)]
    [InlineData("0.000001", 0.000001)]
    [InlineData("1000000", 1_000_000)]
    public void Amount_IsParsedFromCommonWritings(string text, double expected)
    {
        Assert.True(SwapCalculator.TryParseAmount(text, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1000001")]
    [InlineData("0.0000001")]      // more than 6 decimals
    [InlineData("1e3")]
    [InlineData("10 20")]
    [InlineData("/start")]
    public void Amount_BadInputIsRejected(string text)
    {
        Assert.False(SwapCalculator.TryParseAmount(text, out _));
    }

    [Fact]
    public void Amount_NullIsRejected()
    {
        Assert.False(SwapCalculator.TryParseAmount(null, out _));
    }

    private static SwapRate Rate(bool buy, string? dex, string? source, decimal amountIn, decimal amountOut, decimal impact = 0m, int? cached = null) =>
        new()
        {
            Buy = buy, Dex = dex, Source = source, AmountIn = amountIn, AmountOut = amountOut, PriceImpactPct = impact, CachedSeconds = cached,
            UsdPerOnyc = buy ? amountIn / amountOut : amountOut / amountIn,
            Routes = dex == null ? ["Whirlpool", "Raydium CLMM"] : [dex]
        };

    private static List<SwapRate> BuyRates() => new()
    {
        Rate(true, "Raydium CLMM", null, 100m, 86.4441m),
        Rate(true, "Whirlpool", null, 100m, 86.4696m, 0.0023m),
        Rate(true, null, "Jupiter", 100m, 86.4696m, 0.0023m),
        Rate(true, null, "Titan", 100m, 86.4699m)
    };

    [Fact]
    public void Message_ListsTheOutputPerDexBestFirst()
    {
        var text = MessageFormatter.FormatCalculator(true, 100m, BuyRates());

        Assert.Contains("🧮 <b>Calculator</b> · 100.00 USDC → ONyc", text);
        Assert.Contains("✅ <b>Titan</b>: 86.4699 ONyc <i>· via Orca, Raydium</i>", text);
        Assert.Contains("▫️ <b>Orca</b>: 86.4696 ONyc", text);
        Assert.Contains("▫️ <b>Raydium</b>: 86.4441 ONyc", text);
        // the DEXes come first (best first), the aggregators after them
        Assert.True(text.IndexOf("<b>Orca</b>: 86.4696", StringComparison.Ordinal) < text.IndexOf("<b>Raydium</b>: 86.4441", StringComparison.Ordinal));
        Assert.True(text.IndexOf("<b>Raydium</b>: 86.4441", StringComparison.Ordinal) < text.IndexOf("<b>Titan</b>", StringComparison.Ordinal));
    }

    [Fact]
    public void Message_SummaryShowsPriceAndTheMinimumAfterSlippage()
    {
        var text = MessageFormatter.FormatCalculator(true, 100m, BuyRates());

        Assert.Contains("🏆 <b>Best: Titan</b>", text);
        Assert.Matches(@"You pay\s+100\.00 USDC", text);
        Assert.Matches(@"You get\s+86\.4699 ONyc", text);
        Assert.Matches(@"Price\s+\$1\.1565 per ONyc", text);
        Assert.Matches(@"Minimum\s+86\.0376 ONyc", text);   // 86.4699 × (1 - 0.5%)
    }

    [Fact]
    public void Message_ShowsPriceImpactOnlyWhenTheBestComesFromJupiter()
    {
        Assert.DoesNotContain("Impact", MessageFormatter.FormatCalculator(true, 100m, BuyRates()));   // Titan leads and has no impact figure

        var jupiterLeads = BuyRates();
        jupiterLeads.RemoveAt(3);
        Assert.Matches(@"Impact\s+0\.00%", MessageFormatter.FormatCalculator(true, 100m, jupiterLeads));
    }

    [Fact]
    public void Message_SellDirectionUsesUsdcOutputWithTwoDecimals()
    {
        var rates = new List<SwapRate> { Rate(false, "Whirlpool", null, 50m, 57.8123m) };

        var text = MessageFormatter.FormatCalculator(false, 50m, rates);

        Assert.Contains("50.0000 ONyc → USDC", text);
        Assert.Contains("<b>Orca</b>: 57.81 USDC", text);
        Assert.Matches(@"Price\s+\$1\.1562 per ONyc", text);
    }

    [Fact]
    public void Message_SaysWhatIsAndIsNotIncluded()
    {
        var text = MessageFormatter.FormatCalculator(true, 100m, BuyRates());

        Assert.Contains("<b>Included</b>: DEX pool fees and the price impact of your trade.", text);
        Assert.Contains("<b>Not included</b>: the Solana network fee", text);
        Assert.Contains("Quotes only, nothing is executed.", text);
    }

    [Fact]
    public void Message_IsValidTelegramHtmlTagsCachedDataAndHandlesNoQuotes()
    {
        var rates = BuyRates();
        rates[0] = rates[0] with { CachedSeconds = 7 };

        var text = MessageFormatter.FormatCalculator(true, 100m, rates);
        Assert.Null(TelegramHtml.Validate(text));
        Assert.Contains("cached data · 7s old", text);

        var none = MessageFormatter.FormatCalculator(true, 100m, new List<SwapRate>());
        Assert.Contains("currently unavailable", none);
        Assert.Null(TelegramHtml.Validate(none));
    }

    [Fact]
    public void Buttons_CalculatorChoiceFitsTheCallbackLimit()
    {
        var start = ReportButtons.StartKeyboard().InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Contains(start, b => b.CallbackData == ReportButtons.Calc);

        var choice = ReportButtons.CalcChoiceKeyboard().InlineKeyboard.SelectMany(r => r).ToList();
        Assert.Equal(new[] { ReportButtons.CalcBuy, ReportButtons.CalcSell }, choice.Select(b => b.CallbackData).ToArray());
        Assert.All(choice, b => Assert.True(System.Text.Encoding.UTF8.GetByteCount(b.CallbackData!) <= 64));
    }
}
