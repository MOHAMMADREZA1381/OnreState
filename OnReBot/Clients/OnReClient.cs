using System.Globalization;
using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

public class OnReClient(HttpClient http)
{
    // The leaderboard and the live stats are the same for everybody, so they are cached briefly and shared.
    // A wallet's own points (GetPointsAsync) are never cached.
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    public const int PageSize = 50;

    private readonly Dictionary<string, (DateTimeOffset FetchedAt, LeaderboardPage Page)> _pages = new();
    private (DateTimeOffset FetchedAt, OnReStats Stats)? _stats;
    private readonly object _lock = new();

    public async Task<OnReResponse> GetPointsAsync(string wallet, CancellationToken ct)
    {
        using var response = await http.GetAsync($"https://rewards.api.onre.finance/api/v1/points/{wallet}", ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public static OnReResponse Parse(string json) =>
        JsonSerializer.Deserialize<OnReResponse>(json, Json.Options)!;

    // ---------- leaderboard ----------

    public async Task<LeaderboardPage> GetLeaderboardPageAsync(int page, int size, CancellationToken ct)
    {
        var key = $"{page}:{size}";
        lock (_lock)
        {
            if (_pages.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.FetchedAt < CacheFor)
                return CopyWithAge(hit.Page, DateTimeOffset.UtcNow - hit.FetchedAt);
        }

        using var response = await http.GetAsync($"https://rewards.api.onre.finance/api/v1/points/leaderboard?page={page}&size={size}", ct);
        response.EnsureSuccessStatusCode();
        var result = ParseLeaderboard(await response.Content.ReadAsStringAsync(ct));

        lock (_lock)
        {
            foreach (var old in _pages.Where(e => DateTimeOffset.UtcNow - e.Value.FetchedAt >= CacheFor).Select(e => e.Key).ToList())
                _pages.Remove(old);
            _pages[key] = (DateTimeOffset.UtcNow, result);
        }
        return result;
    }

    public static LeaderboardPage ParseLeaderboard(string json) =>
        (JsonSerializer.Deserialize<LeaderboardResponse>(json, Json.Options) ?? new()).Leaderboard;

    private static LeaderboardPage CopyWithAge(LeaderboardPage page, TimeSpan age) =>
        new() { Content = page.Content, TotalElements = page.TotalElements, CachedSeconds = (int)age.TotalSeconds };

    // How many wallets have points (one tiny request, cached)
    public async Task<long> GetTotalUsersAsync(CancellationToken ct) =>
        (await GetLeaderboardPageAsync(0, 1, ct)).TotalElements;

    // Where a wallet stands: the wallets around it and the next goals above it. Everything comes from a few cached leaderboard pages.
    public async Task<LeaderboardPosition> GetPositionAsync(string wallet, OnReResponse me, CancellationToken ct)
    {
        var total = await GetTotalUsersAsync(ct);
        var rank = me.Rank;
        var lastPage = (int)((Math.Max(total, 1) - 1) / PageSize);
        var page = Math.Clamp((rank - 1) / PageSize, 0, lastPage);

        // The page of the wallet plus its neighbours, so a few rows above and below are always there, also at a page edge.
        var pages = new List<LeaderboardPage>();
        foreach (var p in new[] { page - 1, page, page + 1 }.Where(p => p >= 0 && p <= lastPage))
            pages.Add(await GetLeaderboardPageAsync(p, PageSize, ct));
        var entries = pages.SelectMany(p => p.Content).OrderBy(e => e.Rank).ToList();

        var neighbours = entries.Where(e => e.Rank >= rank - 2 && e.Rank <= rank + 2).ToList();
        var next = entries.FirstOrDefault(e => e.Rank == rank - 1);

        // The two closest "round" goals above the wallet
        var goals = new List<LeaderboardEntry>();
        foreach (var target in new[] { 1000, 500, 100, 50, 10 }.Where(t => t < rank).Take(2))
        {
            var goalPage = (await GetLeaderboardPageAsync((target - 1) / PageSize, PageSize, ct)).Content;
            if (goalPage.FirstOrDefault(e => e.Rank == target) is { } goal)
                goals.Add(goal);
        }

        var cached = pages.Where(p => p.CachedSeconds != null).Select(p => p.CachedSeconds!.Value).DefaultIfEmpty(-1).Max();
        return new LeaderboardPosition
        {
            Rank = rank,
            Points = me.TotalPoints,
            TotalUsers = total,
            Neighbours = neighbours,
            NextRankPoints = next?.TotalPoints,
            Goals = goals,
            CachedSeconds = cached >= 0 ? cached : null
        };
    }

    // ---------- live numbers (NAV, APY, AUM) ----------

    public async Task<OnReStats> GetStatsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_stats is { } hit && DateTimeOffset.UtcNow - hit.FetchedAt < CacheFor)
                return new OnReStats { Nav = hit.Stats.Nav, Apy = hit.Stats.Apy, Aum = hit.Stats.Aum, CachedSeconds = (int)(DateTimeOffset.UtcNow - hit.FetchedAt).TotalSeconds };
        }

        var nav = ReadNumberAsync("live-nav", ct);
        var apy = ReadNumberAsync("live-apy", ct);
        var aum = ReadNumberAsync("live-tvl", ct);
        await Task.WhenAll(nav, apy, aum);
        var stats = new OnReStats { Nav = nav.Result, Apy = apy.Result, Aum = aum.Result };

        // Only cache a complete set, so a failed request is tried again next time
        if (stats.Nav != null && stats.Apy != null && stats.Aum != null)
            lock (_lock)
                _stats = (DateTimeOffset.UtcNow, stats);
        return stats;
    }

    private async Task<decimal?> ReadNumberAsync(string name, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"https://core.api.onre.finance/data/{name}", ct);
            response.EnsureSuccessStatusCode();
            return ParseNumber(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[OnRe stats] {name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // The endpoints return a bare number, for example "0.1102"
    public static decimal? ParseNumber(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
