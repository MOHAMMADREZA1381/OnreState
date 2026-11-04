using System.Text.Json;
using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Services;

// Takes a snapshot of OnRe's public leaderboard every hour.
// The leaderboard is read the way the OnRe app reads it (rewards.api.onre.finance/api/v1/points/leaderboard), 2,000 wallets per request:
// about 13 requests, one per second, once an hour.
public class SnapshotJob(HttpClient http, OnReClient onRe, SnapshotStore store)
{
    private const int PageSize = 2000;
    private const int MaxPages = 100;   // a sanity limit: OnRe has about 13 pages today
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(55);
    private static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BetweenRequests = TimeSpan.FromMilliseconds(1100);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);   // the bot gets going first
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var latest = store.LatestTime();
                    if (latest == null || DateTimeOffset.UtcNow - latest.Value >= Every)
                    {
                        var snapshot = await TakeAsync(ct);
                        store.Save(snapshot);
                        var pruned = store.Prune(DateTimeOffset.UtcNow);
                        Console.WriteLine($"[Snapshots] Saved {snapshot.Entries.Count:N0} wallets at {snapshot.At:u} (pruned {pruned})");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[Snapshots] {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(Pause, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // the bot is stopping
        }
    }

    // Reads every page of the leaderboard. Nothing is saved unless all pages were read, so a snapshot is always complete.
    public async Task<Snapshot> TakeAsync(CancellationToken ct)
    {
        var entries = new List<SnapshotEntry>();
        var totalPages = 1;

        for (var page = 0; page < totalPages; page++)
        {
            if (page > 0)
                await Task.Delay(BetweenRequests, ct);

            using var response = await http.GetAsync($"https://rewards.api.onre.finance/api/v1/points/leaderboard?page={page}&size={PageSize}", ct);
            response.EnsureSuccessStatusCode();
            var parsed = ParsePage(await response.Content.ReadAsStringAsync(ct));

            totalPages = Math.Clamp(parsed.TotalPages, 1, MaxPages);
            entries.AddRange(parsed.Entries);
        }

        // A tiny or sharply smaller list means something is wrong with the API, not that the wallets disappeared
        var previous = store.Latest();
        if (entries.Count < 100 || (previous != null && entries.Count < previous.Entries.Count / 2))
            throw new InvalidOperationException($"leaderboard looks wrong ({entries.Count} wallets)");

        var stats = await onRe.GetStatsAsync(ct);
        return new Snapshot
        {
            At = TruncateToMinute(DateTimeOffset.UtcNow),
            Nav = stats.Nav,
            Apy = stats.Apy,
            Aum = stats.Aum,
            Entries = entries
        };
    }

    public static (List<SnapshotEntry> Entries, int TotalPages) ParsePage(string json)
    {
        var page = JsonSerializer.Deserialize<SnapshotPageResponse>(json, Json.Options)?.Leaderboard ?? new();
        return (page.Content.Select(e => new SnapshotEntry(e.Rank, e.Address, e.TotalPoints)).ToList(), page.TotalPages);
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset at) =>
        new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, TimeSpan.Zero);

}

public class SnapshotPageResponse
{
    public SnapshotPage Leaderboard { get; set; } = new();
}

public class SnapshotPage
{
    public List<SnapshotRow> Content { get; set; } = new();
    public int TotalPages { get; set; }
}

public class SnapshotRow
{
    public int Rank { get; set; }
    public string Address { get; set; } = "";
    public decimal TotalPoints { get; set; }
}
