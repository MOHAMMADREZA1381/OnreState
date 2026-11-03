using System.Text.Json;
using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Tests;

public class CardTests
{
    private const string Wallet = "TestWa11etAddressForUnitTestsJust1111";

    private static OnReResponse Response() => new()
    {
        Rank = 1380,
        Address = Wallet,
        TotalPoints = 23_809_123m,
        PointsBreakdown = new OnRePointsBreakdown
        {
            Kamino = new() { ["market"] = 150m },
            Exponent = new ExponentPoints { Yt = 21_000_000m, Lp = 542_213m },
            Loopscale = new() { ["vault"] = 82m },
            ReferralBonus = 2_263_302m
        }
    };

    [Fact]
    public void Build_ListsPlatformsLargestFirstAndSkipsZeroOnes()
    {
        var card = CardClient.Build(Wallet, Response());

        Assert.Equal(["Exponent", "Referral bonus", "Kamino", "Loopscale"], card.Platforms.Select(p => p.Name).ToArray());
        Assert.Equal(21_542_213m, card.Platforms[0].Points);   // YT and LP points are added
        Assert.Equal(1380, card.Rank);
        Assert.Equal(23_809_123m, card.TotalPoints);
        Assert.Equal(Wallet, card.Wallet);
    }

    [Fact]
    public void Build_SumsTheSmallestPlatformsIntoOtherBeyondFiveRows()
    {
        var response = Response();
        response.PointsBreakdown.Ratex = 40m;
        response.PointsBreakdown.Carrot = 30m;
        response.PointsBreakdown.Elemental = 20m;

        var card = CardClient.Build(Wallet, response);

        Assert.Equal(5, card.Platforms.Count);
        Assert.Equal(["Exponent", "Referral bonus", "Kamino", "Loopscale", "Other"], card.Platforms.Select(p => p.Name).ToArray());
        Assert.Equal(90m, card.Platforms[4].Points);   // Ratex 40 + Carrot 30 + Elemental 20
    }

    [Fact]
    public void Build_WalletWithNoPlatformPointsHasNoRows()
    {
        var card = CardClient.Build(Wallet, new OnReResponse { Rank = 5, TotalPoints = 0m });

        Assert.Empty(card.Platforms);
    }

    [Fact]
    public void Request_IsSerializedWithTheCamelCaseNamesTheNodeServiceExpects()
    {
        var json = JsonSerializer.Serialize(CardClient.Build(Wallet, Response()), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"wallet\":\"" + Wallet + "\"", json);
        Assert.Contains("\"rank\":1380", json);
        Assert.Contains("\"totalPoints\":23809123", json);
        Assert.Contains("\"platforms\":[{\"name\":\"Exponent\",\"points\":21542213}", json);
    }

    [Fact]
    public void Button_CarriesTheWalletAndFitsTheCallbackLimit()
    {
        var wallet = new string('A', 44);

        var data = ReportButtons.CardData(wallet);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(ReportButtons.TryParseCard(data, out var parsed));
        Assert.Equal(wallet, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("card:")]
    [InlineData("card:not-a-wallet")]
    [InlineData("full:TestWa11etAddressForUnitTestsJust1111")]
    public void Button_RejectsOtherData(string? data)
    {
        Assert.False(ReportButtons.TryParseCard(data, out _));
    }

    [Fact]
    public void SummaryKeyboard_HasShareCardNextToFullReport()
    {
        var buttons = ReportButtons.Keyboard(Wallet).InlineKeyboard.SelectMany(r => r).ToList();

        Assert.Contains(buttons, b => b.CallbackData == ReportButtons.FullData(Wallet));
        Assert.Contains(buttons, b => b.CallbackData == ReportButtons.Decline);
        Assert.Contains(buttons, b => b.CallbackData == ReportButtons.CardData(Wallet));
        Assert.All(buttons, b => Assert.True(System.Text.Encoding.UTF8.GetByteCount(b.CallbackData!) <= 64));
    }
}
