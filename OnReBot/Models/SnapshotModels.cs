namespace OnReBot.Models;

// One row of the leaderboard as it was at the time of a snapshot. Only what OnRe shows publicly on its leaderboard.
public record SnapshotEntry(int Rank, string Address, decimal Points);

// A copy of OnRe's public leaderboard (every wallet with points) plus OnRe's live numbers at that moment. The bot takes one every hour,
// which gives the history that the OnRe API does not offer. Nothing about the bot's own users is in it.
public class Snapshot
{
    public DateTimeOffset At { get; set; }
    public decimal? Nav { get; set; }
    public decimal? Apy { get; set; }
    public decimal? Aum { get; set; }
    public List<SnapshotEntry> Entries { get; set; } = new();

    private Dictionary<string, SnapshotEntry>? _byAddress;

    public IReadOnlyDictionary<string, SnapshotEntry> ByAddress =>
        _byAddress ??= Entries.GroupBy(e => e.Address).ToDictionary(g => g.Key, g => g.First());

    // The wallet at exactly this rank, if the snapshot has it
    public SnapshotEntry? AtRank(int rank) => Entries.FirstOrDefault(e => e.Rank == rank);
}

// How a wallet moved between two snapshots.
public class Momentum
{
    public TimeSpan Window { get; set; }

    // The wallet was not on the leaderboard at the older snapshot
    public bool IsNew { get; set; }
    public int OldRank { get; set; }
    public int NewRank { get; set; }
    public decimal OldPoints { get; set; }
    public decimal NewPoints { get; set; }

    public decimal PointsGained => NewPoints - OldPoints;

    // Positive: moved up the leaderboard
    public int RanksClimbed => OldRank - NewRank;

    public decimal PointsPerDay => Window.TotalHours <= 0 ? 0 : PointsGained * 24m / (decimal)Window.TotalHours;
}

// A wallet in a "movers" list.
public record Mover(string Address, int OldRank, int NewRank, decimal PointsGained, bool IsNew);

// The result of looking up a wallet's momentum.
public class MomentumResult
{
    // What happened: not enough snapshots yet, the wallet is not on the leaderboard, or a normal result
    public MomentumStatus Status { get; set; }
    public Momentum? Day { get; set; }
    public Momentum? Week { get; set; }

    // Rank goals above the wallet with the number of days to reach them at the pace of the last day (null: not reachable at this pace)
    public List<(int Rank, decimal? Days)> Goals { get; set; } = new();

    // How long until a first comparison is possible (only for NotEnoughData)
    public TimeSpan? WaitFor { get; set; }

    // How old the newest snapshot is
    public TimeSpan SnapshotAge { get; set; }
}

public enum MomentumStatus
{
    Ok,
    NotEnoughData,
    NotOnLeaderboard
}

public class MoversResult
{
    public bool NotEnoughData { get; set; }
    public TimeSpan SnapshotAge { get; set; }
    public TimeSpan Window { get; set; }
    public List<Mover> Gainers { get; set; } = new();
    public List<Mover> Climbers { get; set; } = new();
    public List<Mover> NewInTop100 { get; set; } = new();
    public long WalletsNow { get; set; }
    public long WalletsBefore { get; set; }
}
