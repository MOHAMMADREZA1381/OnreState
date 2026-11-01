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
            var obligations = ParseObligations(await GetJsonAsync(ObligationsUrl(market.Address, wallet), ct));
            var position = BuildPosition(market.Name, obligations, config.KaminoReserves);
            if (position != null)
                positions.Add(position);
        }
        return positions;
    }

    // Net value over time of every active obligation (Kamino keeps daily snapshots).
    public async Task<List<KaminoHistory>> GetHistoryAsync(string wallet, CancellationToken ct)
    {
        var result = new List<KaminoHistory>();
        foreach (var market in config.KaminoMarkets)
        {
            var obligations = ParseObligations(await GetJsonAsync(ObligationsUrl(market.Address, wallet), ct));
            foreach (var obligation in obligations.Where(IsActive))
            {
                var url = $"https://api.kamino.finance/v2/kamino-market/{market.Address}/obligations/{obligation.ObligationAddress}/metrics/history";
                var history = BuildHistory(market.Name, await GetJsonAsync(url, ct));
                if (history != null)
                    result.Add(history);
            }
        }
        return result;
    }

    private static string ObligationsUrl(string market, string wallet) =>
        $"https://api.kamino.finance/kamino-market/{market}/users/{wallet}/obligations?env=mainnet-beta";

    // One place for the rate-limit handling: after a 429 no request is sent until the wait is over.
    private async Task<string> GetJsonAsync(string url, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < _blockedUntil)
            throw new HttpRequestException("Kamino rate limited");

        using var response = await http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            _blockedUntil = DateTimeOffset.UtcNow + wait;
            throw new HttpRequestException("Kamino returned 429");
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct);
    }

    public static List<KaminoObligation> ParseObligations(string json) =>
        JsonSerializer.Deserialize<List<KaminoObligation>>(json, Json.Options) ?? new();

    // Summarizes the daily snapshots as first and last net value; null when there is no data at all.
    public static KaminoHistory? BuildHistory(string marketName, string json)
    {
        var points = (JsonSerializer.Deserialize<KaminoHistoryResponse>(json, Json.Options)?.History ?? new())
            .OrderBy(p => p.Timestamp)
            .ToList();
        if (points.Count == 0)
            return null;

        return new KaminoHistory
        {
            MarketName = marketName,
            From = points[0].Timestamp,
            To = points[^1].Timestamp,
            FirstNetValueUsd = ToDecimal(points[0].RefreshedStats.NetAccountValue),
            LastNetValueUsd = ToDecimal(points[^1].RefreshedStats.NetAccountValue),
            Points = points.Count
        };
    }

    private static bool IsActive(KaminoObligation obligation) =>
        ToDecimal(obligation.RefreshedStats.UserTotalDeposit) > 0 || ToDecimal(obligation.RefreshedStats.UserTotalBorrow) > 0;

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
