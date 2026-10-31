using System.Globalization;
using System.Net;
using System.Text.Json;
using OnReBot.Config;
using OnReBot.Models;

namespace OnReBot.Clients;

public class KaminoClient(HttpClient http, BotConfig config)
{
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public async Task<List<KaminoPosition>> GetPositionsAsync(string wallet, CancellationToken ct)
    {
        var positions = new List<KaminoPosition>();
        foreach (var market in config.KaminoMarkets)
        {
            if (DateTimeOffset.UtcNow < _blockedUntil)
                throw new HttpRequestException("Kamino rate limited");

            var url = $"https://api.kamino.finance/kamino-market/{market.Address}/users/{wallet}/obligations?env=mainnet-beta";
            using var response = await http.GetAsync(url, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                _blockedUntil = DateTimeOffset.UtcNow + wait;
                throw new HttpRequestException("Kamino returned 429");
            }
            response.EnsureSuccessStatusCode();

            var obligations = ParseObligations(await response.Content.ReadAsStringAsync(ct));
            var position = BuildPosition(market.Name, obligations, config.KaminoReserves);
            if (position != null)
                positions.Add(position);
        }
        return positions;
    }

    public static List<KaminoObligation> ParseObligations(string json) =>
        JsonSerializer.Deserialize<List<KaminoObligation>>(json, Json.Options) ?? new();

    public static KaminoPosition? BuildPosition(string marketName, List<KaminoObligation> obligations, List<KaminoReserve> reserves)
    {
        var position = new KaminoPosition { MarketName = marketName };
        var active = false;
        decimal ltvWeighted = 0;

        foreach (var obligation in obligations)
        {
            var deposit = ToDecimal(obligation.RefreshedStats.UserTotalDeposit);
            var borrow = ToDecimal(obligation.RefreshedStats.UserTotalBorrow);
            if (deposit <= 0 && borrow <= 0)
                continue;

            active = true;
            position.TotalDepositUsd += deposit;
            position.TotalBorrowUsd += borrow;
            position.NetValueUsd += ToDecimal(obligation.RefreshedStats.NetAccountValue);
            ltvWeighted += ToDecimal(obligation.RefreshedStats.LoanToValue) * deposit;

            foreach (var d in obligation.State.Deposits)
            {
                var reserve = reserves.FirstOrDefault(r => r.Address == d.DepositReserve);
                if (reserve == null)
                    continue;

                var amount = ToDecimal(d.DepositedAmount) / Pow10(reserve.Decimals);
                if (amount <= 0)
                    continue;

                var token = position.Tokens.FirstOrDefault(t => t.Symbol == reserve.Symbol);
                if (token == null)
                    position.Tokens.Add(new KaminoTokenAmount { Symbol = reserve.Symbol, Amount = amount });
                else
                    token.Amount += amount;
            }
        }

        if (!active)
            return null;

        if (position.TotalDepositUsd > 0)
            position.LoanToValue = ltvWeighted / position.TotalDepositUsd;
        return position;
    }

    private static decimal ToDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static decimal Pow10(int decimals)
    {
        decimal result = 1;
        for (var i = 0; i < decimals; i++)
            result *= 10;
        return result;
    }
}
