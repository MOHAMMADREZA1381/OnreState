using OnReBot.Clients;
using OnReBot.Config;

namespace OnReBot.Tests;

public class ParsingTests
{
    private const string Wallet = "TestWa11etAddressForUnitTestsJust1111";

    private static string Read(string name) => File.ReadAllText(Path.Combine("TestData", name));

    private static readonly List<KaminoReserve> Reserves = new()
    {
        new() { Address = "6ZxkBSJEqsXA3Kdm2PDAzHLUdPTPUK93Lf4bAezec1UQ", Symbol = "ONyc", Decimals = 9 }
    };

    [Fact]
    public void OnRe_ParsesRankTotalAndDynamicMarkets()
    {
        var r = OnReClient.Parse(Read("onre_points.json"));

        Assert.Equal(Wallet, r.Address);
        Assert.True(r.Rank > 0);
        Assert.True(r.TotalPoints > 0);
        Assert.True(r.PointsBreakdown.Exponent.Markets.ContainsKey("ONYC-JAN2027"));
        Assert.True(r.PointsBreakdown.Exponent.Markets["ONYC-JAN2027"].Yt > 0);
    }

    [Fact]
    public void Kamino_BuildsActivePositionFromRefreshedStats()
    {
        var obligations = KaminoClient.ParseObligations(Read("kamino_obligations.json"));
        var p = KaminoClient.BuildPosition("OnRe Market", obligations, Reserves);

        Assert.NotNull(p);
        Assert.Equal(10m, Assert.Single(p!.Tokens).Amount);
        Assert.Equal("ONyc", p.Tokens[0].Symbol);
        Assert.Equal(11.6048304m, p.TotalDepositUsd);
        Assert.Equal(0m, p.TotalBorrowUsd);
    }

    [Fact]
    public void Kamino_EmptyResponseMeansNoPosition()
    {
        var obligations = KaminoClient.ParseObligations(Read("kamino_empty.json"));
        Assert.Null(KaminoClient.BuildPosition("OnRe Market", obligations, Reserves));
    }

    [Fact]
    public void Loopscale_FindsWalletInVaultDeposits()
    {
        var deposits = LoopscaleClient.ParseDeposits(Read("loopscale_deposits.json"), "7PeYxZpM2dpc4RRDQovexMJ6tkSVLWtRN4mbNywsU3e6");

        Assert.Equal(3, deposits.Count);
        Assert.Equal(5984968, deposits[Wallet]);
        Assert.False(deposits.ContainsKey("AnotherWa11etAddressForUnitTestsJust11"));
    }
}
