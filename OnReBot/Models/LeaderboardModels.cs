namespace OnReBot.Models;

// OnRe's public leaderboard (rewards.api.onre.finance/api/v1/points/leaderboard), the same list the OnRe app shows.
public class LeaderboardResponse
{
    public LeaderboardPage Leaderboard { get; set; } = new();
}

public class LeaderboardPage
{
    public List<LeaderboardEntry> Content { get; set; } = new();

    // How many wallets have points in total
    public long TotalElements { get; set; }

    // null: fetched just now; a number: served from the short shared cache (set by the client, not part of the JSON)
    public int? CachedSeconds { get; set; }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string Address { get; set; } = "";
    public decimal TotalPoints { get; set; }
    public OnRePointsBreakdown PointsBreakdown { get; set; } = new();
}

// One platform in the mix of points of a group of wallets.
public record MixRow(string Name, decimal Points, decimal Share, int Wallets);

// Where the points of a group of wallets (for example the top 50) come from.
public static class PlatformMix
{
    // Short names for the table; Carrot and Carrot Lending are one platform here.
    private static string Display(string name) => name switch
    {
        "Permissionless Boost" => "Boost",
        "Carrot Lending" => "Carrot",
        "Exponent Vault" => "Exp. Vault",
        "Referral bonus" => "Referral",
        _ => name
    };

    // Each platform's share is a share of the sum of the platform points (so the shares add up to 100%); Wallets counts how many
    // wallets of the group earned anything there. Largest first.
    public static List<MixRow> Build(IReadOnlyCollection<LeaderboardEntry> entries)
    {
        var points = new Dictionary<string, decimal>();
        var wallets = new Dictionary<string, int>();

        foreach (var entry in entries)
        {
            var perWallet = entry.PointsBreakdown.Platforms()
                .GroupBy(p => Display(p.Name))
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Points));
            foreach (var (name, value) in perWallet)
            {
                points[name] = points.GetValueOrDefault(name) + value;
                wallets[name] = wallets.GetValueOrDefault(name) + 1;
            }
        }

        var total = points.Values.Sum();
        return points
            .OrderByDescending(p => p.Value)
            .Select(p => new MixRow(p.Key, p.Value, total > 0 ? p.Value / total : 0, wallets[p.Key]))
            .ToList();
    }
}

// OnRe's live numbers (core.api.onre.finance/data/...): the same ones the OnRe app shows. A value is null when its request failed.
public class OnReStats
{
    public decimal? Nav { get; set; }
    public decimal? Apy { get; set; }
    public decimal? Aum { get; set; }
    public int? CachedSeconds { get; set; }
}

// Where a wallet stands on the leaderboard: its neighbours and how far the next goals are.
public class LeaderboardPosition
{
    public int Rank { get; set; }
    public decimal Points { get; set; }
    public long TotalUsers { get; set; }

    // The wallets around the user (a few above and below), in rank order. The user's own row is the one with Address == the wallet.
    public List<LeaderboardEntry> Neighbours { get; set; } = new();

    // Points of the wallet that is one rank above, when known
    public decimal? NextRankPoints { get; set; }

    // Rank goals above the user (for example 1,000 and 500) with the points of the wallet at that rank
    public List<LeaderboardEntry> Goals { get; set; } = new();
    public int? CachedSeconds { get; set; }
}
