using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Tests;

public class MarketsTests
{
    // Shape of the Node service's /overview response (values from a real call)
    private const string OverviewJson = """
    {"markets":[{"name":"ONyc 10JAN27","impliedApy":0.1272079437954361,"underlyingApy":0.11018567209566021,"ytPrice":0.022160016692331475,"ptPrice":0.9778399833076685,"tokenPrice":1.162146115,"maturity":"2027-01-10T13:00:00.000Z"},
                {"name":"srONyc 10JAN27","impliedApy":null,"underlyingApy":0.0757,"ytPrice":0.0149,"ptPrice":0.985,"tokenPrice":1.0287,"maturity":"2027-01-10T11:00:00.000Z"}],
     "tranches":[{"symbol":"ONyc","seniorApy":0.0774,"juniorApy":0.1538}],"cachedAgeSeconds":4}
    """;

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

    private static ExponentOverview Overview() => JsonSerializer.Deserialize<ExponentOverview>(OverviewJson, Json.Options)!;

    [Fact]
    public void Overview_IsReadFromTheServiceJson()
    {
        var overview = Overview();

        Assert.Equal(2, overview.Markets.Count);
        Assert.Equal(0.1272079437954361m, overview.Markets[0].ImpliedApy);
        Assert.Null(overview.Markets[1].ImpliedApy);
        Assert.Equal("ONyc", overview.Tranches[0].Symbol);
        Assert.Equal(4, overview.CachedAgeSeconds);
    }

    [Fact]
    public void Message_ShowsApyPricesAndDaysToMaturityPerMarket()
    {
        var text = MessageFormatter.FormatMarkets(Overview(), false, Now);

        Assert.Contains("📊 <b>OnRe Markets</b>", text);
        Assert.Contains("🌾 <b>Exponent</b> · <i>ONyc 10JAN27</i>", text);
        Assert.Matches(@"Token price\s+\$1\.1621", text);
        Assert.Matches(@"Implied APY\s+12\.72%", text);
        Assert.Matches(@"Underlying\s+11\.02%", text);
        Assert.Matches(@"YT price\s+\$0\.0222", text);
        Assert.Matches(@"PT price\s+\$0\.9778", text);
        Assert.Matches(@"Matures\s+10 Jan 2027 \(69 days\)", text);
    }

    [Fact]
    public void Message_ShowsTrancheApysAndMarksMissingValues()
    {
        var text = MessageFormatter.FormatMarkets(Overview(), false, Now);

        Assert.Contains("🛡 <b>Exponent Protected</b> · <i>ONyc</i>", text);
        Assert.Matches(@"Senior APY\s+7\.74%", text);
        Assert.Matches(@"Junior APY\s+15\.38%", text);
        Assert.Matches(@"Implied APY\s+–", text);   // the srONyc market has no implied APY
    }

    [Fact]
    public void Message_TagsCachedDataAndIsValidTelegramHtml()
    {
        var text = MessageFormatter.FormatMarkets(Overview(), false, Now);

        Assert.Contains("cached data · 4s old", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Message_FailureSaysUnavailable()
    {
        var text = MessageFormatter.FormatMarkets(null, true, Now);

        Assert.Contains("currently unavailable", text);
        Assert.Null(TelegramHtml.Validate(text));
    }

    [Fact]
    public void Message_NoMarketsSaysSo()
    {
        var text = MessageFormatter.FormatMarkets(new ExponentOverview(), false, Now);

        Assert.Contains("No active OnRe markets found.", text);
    }

    [Fact]
    public void MarketsButton_FitsTelegramCallbackLimitAndKeepsTheRankButton()
    {
        var keyboard = ReportButtons.StartKeyboard().InlineKeyboard.SelectMany(r => r).ToList();

        Assert.Contains(keyboard, b => b.CallbackData == ReportButtons.Rank);
        Assert.Contains(keyboard, b => b.CallbackData == ReportButtons.Markets);
        Assert.All(keyboard, b => Assert.True(System.Text.Encoding.UTF8.GetByteCount(b.CallbackData!) <= 64));
    }
}
