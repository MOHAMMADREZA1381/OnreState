using OnReBot.Models;

namespace OnReBot.Services;

// Compares snapshots of OnRe's public leaderboard: how fast a wallet gains points, who moves the most. All of it is plain arithmetic on
// two snapshots, so it can be tested without any network or disk.
public static class TimeMachine
{
    // A comparison needs at least this much time between the two snapshots, otherwise the "per day" numbers would be noise.
    public static readonly TimeSpan MinWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan DayTarget = TimeSpan.FromHours(24);
    private static readonly TimeSpan DayMax = TimeSpan.FromHours(26);
    private static readonly TimeSpan WeekTarget = TimeSpan.FromDays(7);
    private static readonly TimeSpan WeekMin = TimeSpan.FromDays(6);
    private static readonly TimeSpan WeekMax = TimeSpan.FromHours(8.5 * 24);
    private static readonly int[] GoalRanks = { 1000, 500, 100, 50, 10 };
    private const int ClimberMaxRank = 5000;

    // The snapshot to compare with: the one whose age is closest to the target, within [min, max]. null when there is none.
    public static DateTimeOffset? PickReference(IReadOnlyList<DateTimeOffset> times, DateTimeOffset latest, TimeSpan target, TimeSpan min, TimeSpan max)
    {
        DateTimeOffset? best = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var t in times)
        {
            var age = latest - t;
            if (age < min || age > max)
                continue;
            var distance = (age - target).Duration();
            if (distance < bestDistance)
            {
                best = t;
                bestDistance = distance;
            }
        }
        return best;
    }

    // ---------- one wallet ----------

    public static Momentum? Compare(Snapshot now, Snapshot old, string wallet)
    {
        if (!now.ByAddress.TryGetValue(wallet, out var current))
            return null;

        var window = now.At - old.At;
        if (old.ByAddress.TryGetValue(wallet, out var before))
            return new Momentum { Window = window, OldRank = before.Rank, NewRank = current.Rank, OldPoints = before.Points, NewPoints = current.Points };

        // A wallet that was not on the leaderboard yet: it started from nothing, at the bottom of the list
        return new Momentum { Window = window, IsNew = true, OldRank = old.Entries.Count + 1, NewRank = current.Rank, OldPoints = 0, NewPoints = current.Points };
    }

    public static ProgressChart GetProgressChart(SnapshotStore store, string wallet, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstDay = today.AddDays(-6);
        var start = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var byDay = store.List().Where(at => at >= start && at <= now)
            .Reverse().ToLookup(at => DateOnly.FromDateTime(at.UtcDateTime));

        var days = new List<ProgressDay>(7);
        for (var date = firstDay; date <= today; date = date.AddDays(1))
        {
            Snapshot? snapshot = null;
            foreach (var at in byDay[date])
            {
                snapshot = store.Load(at);
                if (snapshot != null)
                    break;
            }

            // Absence from a real snapshot is a gap, including when an earlier snapshot that day had the wallet.
            var entry = snapshot?.Entries.Find(entry => entry.Address == wallet);
            days.Add(entry is { Rank: > 0, Points: >= 0 }
                ? new ProgressDay(date, entry.Points, entry.Rank)
                : new ProgressDay(date, null, null));
        }

        return new ProgressChart(wallet, days);
    }

    public static MomentumResult GetMomentum(SnapshotStore store, string wallet)
    {
        var times = store.List();
        if (times.Count == 0 || store.Load(times[^1]) is not { } latest)
            return new MomentumResult { Status = MomentumStatus.NotEnoughData, WaitFor = MinWindow };

        var latestAt = times[^1];
        var dayAt = PickReference(times, latestAt, DayTarget, MinWindow, DayMax);
        if (dayAt == null)
        {
            // How long until the oldest snapshot is old enough
            var wait = MinWindow - (latestAt - times[0]);
            return new MomentumResult { Status = MomentumStatus.NotEnoughData, WaitFor = wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(5) };
        }

        if (!latest.ByAddress.ContainsKey(wallet))
            return new MomentumResult { Status = MomentumStatus.NotOnLeaderboard };

        var day = store.Load(dayAt.Value) is { } dayOld ? Compare(latest, dayOld, wallet) : null;
        if (day == null)
            return new MomentumResult { Status = MomentumStatus.NotEnoughData, WaitFor = TimeSpan.FromMinutes(5) };

        var weekAt = PickReference(times, latestAt, WeekTarget, WeekMin, WeekMax);
        var week = weekAt != null && store.Load(weekAt.Value) is { } weekOld ? Compare(latest, weekOld, wallet) : null;

        return new MomentumResult { Status = MomentumStatus.Ok, Day = day, Week = week, Goals = Goals(latest, day), SnapshotAge = DateTimeOffset.UtcNow - latestAt };
    }

    // The two nearest round ranks above the wallet and how many days they are away at the pace of the last day. null days: not reachable at
    // this pace (the wallet is not gaining points). The wallets at those ranks keep earning too, so this is a rough guide only.
    public static List<(int Rank, decimal? Days)> Goals(Snapshot latest, Momentum day)
    {
        var goals = new List<(int, decimal?)>();
        foreach (var rank in GoalRanks.Where(r => r < day.NewRank).Take(2))
        {
            if (latest.AtRank(rank) is not { } target)
                continue;
            var needed = target.Points - day.NewPoints;
            if (needed <= 0)
                continue;
            goals.Add((rank, day.PointsPerDay > 0 ? needed / day.PointsPerDay : null));
        }
        return goals;
    }

    // ---------- everybody ----------

    public static MoversResult GetMovers(SnapshotStore store, int count = 10)
    {
        var times = store.List();
        if (times.Count == 0 || store.Load(times[^1]) is not { } latest)
            return new MoversResult { NotEnoughData = true };

        var refAt = PickReference(times, times[^1], DayTarget, MinWindow, DayMax);
        if (refAt == null || store.Load(refAt.Value) is not { } old)
            return new MoversResult { NotEnoughData = true };

        var result = Movers(latest, old, count);
        result.SnapshotAge = DateTimeOffset.UtcNow - times[^1];
        return result;
    }

    public static MoversResult Movers(Snapshot now, Snapshot old, int count)
    {
        var gainers = new List<Mover>();
        var climbers = new List<Mover>();
        var newTop = new List<Mover>();

        foreach (var entry in now.Entries)
        {
            var known = old.ByAddress.TryGetValue(entry.Address, out var before);
            var gained = entry.Points - (known ? before!.Points : 0m);
            var oldRank = known ? before!.Rank : old.Entries.Count + 1;
            var mover = new Mover(entry.Address, oldRank, entry.Rank, gained, !known);

            if (gained > 0)
                gainers.Add(mover);
            if (known && entry.Rank <= ClimberMaxRank && oldRank - entry.Rank > 0)
                climbers.Add(mover);
            if (entry.Rank <= 100 && (!known || oldRank > 100))
                newTop.Add(mover);
        }

        return new MoversResult
        {
            Window = now.At - old.At,
            Gainers = gainers.OrderByDescending(m => m.PointsGained).Take(count).ToList(),
            Climbers = climbers.OrderByDescending(m => m.OldRank - m.NewRank).ThenBy(m => m.NewRank).Take(count).ToList(),
            NewInTop100 = newTop.OrderBy(m => m.NewRank).Take(count).ToList(),
            WalletsNow = now.Entries.Count,
            WalletsBefore = old.Entries.Count
        };
    }
}
