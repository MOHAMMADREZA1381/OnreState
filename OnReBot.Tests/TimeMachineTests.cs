using OnReBot.Models;
using OnReBot.Services;

namespace OnReBot.Tests;

public class TimeMachineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "onrebot-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SnapshotStore _store;
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public TimeMachineTests() => _store = new SnapshotStore(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // A snapshot with the given wallets (address -> points); ranks follow the points, like OnRe's list.
    private static Snapshot Snap(DateTimeOffset at, params (string Address, decimal Points)[] wallets)
    {
        var entries = wallets.OrderByDescending(w => w.Points).Select((w, i) => new SnapshotEntry(i + 1, w.Address, w.Points)).ToList();
        return new Snapshot { At = at, Nav = 1.16m, Apy = 0.11m, Aum = 285_000_000m, Entries = entries };
    }

    // ---------- the store ----------

    [Fact]
    public void Store_RoundTripsASnapshotThroughAGzipFile()
    {
        var snapshot = Snap(Now, ("AddrA", 1_000_000m), ("AddrB", 500m));
        snapshot.Nav = null;   // an unknown number is kept as unknown

        _store.Save(snapshot);
        var loaded = new SnapshotStore(_dir).Load(Now)!;   // a new store, so nothing comes from memory

        Assert.Equal(Now, loaded.At);
        Assert.Null(loaded.Nav);
        Assert.Equal(0.11m, loaded.Apy);
        Assert.Equal(285_000_000m, loaded.Aum);
        Assert.Equal(new[] { ("AddrA", 1, 1_000_000m), ("AddrB", 2, 500m) }, loaded.Entries.Select(e => (e.Address, e.Rank, e.Points)).ToArray());
        Assert.Equal(Now, new SnapshotStore(_dir).LatestTime());
    }

    [Fact]
    public void Store_ListsSnapshotsOldestFirstAndIgnoresHalfWrittenFiles()
    {
        _store.Save(Snap(Now, ("A", 1m)));
        _store.Save(Snap(Now.AddHours(-3), ("A", 1m)));
        File.WriteAllText(Path.Combine(_dir, "20261104T0900Z.snap.gz.tmp"), "half");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");

        Assert.Equal(new[] { Now.AddHours(-3), Now }, _store.List());
    }

    [Fact]
    public void Store_ACorruptFileIsSkippedInsteadOfBreakingEverything()
    {
        File.WriteAllText(Path.Combine(_dir, "20261104T1000Z.snap.gz"), "this is not gzip");

        Assert.Null(_store.Load(Now.AddHours(-2)));
    }

    [Fact]
    public void Prune_KeepsEverythingFor48HoursThenOneSnapshotPerDayFor30Days()
    {
        // hourly snapshots over the last 5 days, plus a very old one
        var times = Enumerable.Range(0, 5 * 24).Select(h => Now.AddHours(-h)).Append(Now.AddDays(-40)).ToList();
        foreach (var t in times)
            _store.Save(Snap(t, ("A", 1m)));

        var deleted = _store.Prune(Now);
        var left = _store.List();

        Assert.True(deleted > 0);
        Assert.DoesNotContain(left, t => Now - t > TimeSpan.FromDays(30));                          // the 40-day-old one is gone
        Assert.Equal(49, left.Count(t => Now - t <= TimeSpan.FromHours(48)));                          // all of the last 48 hours (49 with both ends)
        var older = left.Where(t => Now - t > TimeSpan.FromHours(48)).ToList();
        Assert.Equal(older.Count, older.Select(t => t.UtcDateTime.Date).Distinct().Count());            // at most one per day
        Assert.True(older.Count >= 2);
    }

    // ---------- choosing the snapshot to compare with ----------

    [Fact]
    public void PickReference_TakesTheSnapshotClosestToTheTargetWithinTheLimits()
    {
        var times = new[] { Now.AddHours(-30), Now.AddHours(-25), Now.AddHours(-23), Now.AddHours(-5), Now };

        Assert.Equal(Now.AddHours(-25), TimeMachine.PickReference(times, Now, TimeSpan.FromHours(24), TimeSpan.FromHours(6), TimeSpan.FromHours(26)));
        Assert.Null(TimeMachine.PickReference(new[] { Now.AddHours(-5), Now }, Now, TimeSpan.FromHours(24), TimeSpan.FromHours(6), TimeSpan.FromHours(26)));   // too recent
        Assert.Null(TimeMachine.PickReference(new[] { Now.AddHours(-40), Now }, Now, TimeSpan.FromHours(24), TimeSpan.FromHours(6), TimeSpan.FromHours(26)));  // too old
    }

    // ---------- one wallet ----------

    [Fact]
    public void Compare_GivesPointsGainedPerDayAndRanksClimbed()
    {
        var old = Snap(Now.AddHours(-12), ("Me", 900m), ("Other1", 1_000m), ("Other2", 800m));
        var now = Snap(Now, ("Me", 1_200m), ("Other1", 1_000m), ("Other2", 800m));

        var m = TimeMachine.Compare(now, old, "Me")!;

        Assert.Equal(300m, m.PointsGained);
        Assert.Equal(600m, m.PointsPerDay);          // 300 points in 12 hours
        Assert.Equal(2, m.OldRank);
        Assert.Equal(1, m.NewRank);
        Assert.Equal(1, m.RanksClimbed);
        Assert.False(m.IsNew);
    }

    [Fact]
    public void Compare_AWalletThatWasNotThereYetStartedFromNothing()
    {
        var old = Snap(Now.AddHours(-24), ("A", 100m), ("B", 50m));
        var now = Snap(Now, ("A", 100m), ("B", 50m), ("New", 70m));

        var m = TimeMachine.Compare(now, old, "New")!;

        Assert.True(m.IsNew);
        Assert.Equal(70m, m.PointsGained);
        Assert.Equal(3, m.OldRank);   // the bottom of the old list, one place below its last wallet
        Assert.Equal(2, m.NewRank);
        Assert.Null(TimeMachine.Compare(now, old, "Nobody"));
    }

    [Fact]
    public void Momentum_WithoutEnoughSnapshotsSaysHowLongToWait()
    {
        Assert.Equal(MomentumStatus.NotEnoughData, TimeMachine.GetMomentum(_store, "Me").Status);   // no snapshot at all

        _store.Save(Snap(Now.AddHours(-2), ("Me", 100m)));
        _store.Save(Snap(Now, ("Me", 110m)));

        var result = TimeMachine.GetMomentum(_store, "Me");

        Assert.Equal(MomentumStatus.NotEnoughData, result.Status);
        Assert.Equal(TimeSpan.FromHours(4), result.WaitFor);   // 6 hours are needed, 2 have passed
    }

    [Fact]
    public void Momentum_CompaniesTheNewestSnapshotWithOneAboutADayOlderAndProjectsTheGoals()
    {
        // Me is at rank 1,500 and gains 200 points a day; the wallet at rank 1,000 has 5,000 points
        var wallets = new List<(string, decimal)> { ("Me", 1_000m) };
        for (var i = 0; i < 1_600; i++)
            wallets.Add(($"W{i}", 5_000m + 3_000m - i));
        var old = Snap(Now.AddHours(-24), wallets.ToArray());
        wallets[0] = ("Me", 1_200m);
        var latest = Snap(Now, wallets.ToArray());
        _store.Save(old);
        _store.Save(latest);

        var result = TimeMachine.GetMomentum(_store, "Me");

        Assert.Equal(MomentumStatus.Ok, result.Status);
        Assert.Equal(200m, result.Day!.PointsGained);
        Assert.Equal(200m, result.Day.PointsPerDay);
        Assert.Null(result.Week);   // no snapshot about a week old
        Assert.Equal(2, result.Goals.Count);
        Assert.Equal(1000, result.Goals[0].Rank);
        var needed = latest.AtRank(1000)!.Points - 1_200m;
        Assert.Equal(needed / 200m, result.Goals[0].Days);
    }

    [Fact]
    public void Momentum_AWalletThatDoesNotGainPointsHasNoDaysToAnyGoal()
    {
        var wallets = new List<(string, decimal)> { ("Me", 100m) };
        for (var i = 0; i < 1_200; i++)
            wallets.Add(($"W{i}", 1_000m + i));
        _store.Save(Snap(Now.AddHours(-24), wallets.ToArray()));
        _store.Save(Snap(Now, wallets.ToArray()));

        var result = TimeMachine.GetMomentum(_store, "Me");

        Assert.Equal(0m, result.Day!.PointsGained);
        Assert.All(result.Goals, g => Assert.Null(g.Days));
    }

    [Fact]
    public void Momentum_AWalletOutsideTheLeaderboardIsSaidSo()
    {
        _store.Save(Snap(Now.AddHours(-24), ("A", 100m)));
        _store.Save(Snap(Now, ("A", 110m)));

        Assert.Equal(MomentumStatus.NotOnLeaderboard, TimeMachine.GetMomentum(_store, "Nobody").Status);
    }

    [Fact]
    public void Momentum_AddsTheWeekWhenASnapshotAboutAWeekOldExists()
    {
        _store.Save(Snap(Now.AddDays(-7), ("Me", 100m), ("A", 500m)));
        _store.Save(Snap(Now.AddHours(-24), ("Me", 300m), ("A", 500m)));
        _store.Save(Snap(Now, ("Me", 400m), ("A", 500m)));

        var result = TimeMachine.GetMomentum(_store, "Me");

        Assert.Equal(100m, result.Day!.PointsGained);
        Assert.Equal(300m, result.Week!.PointsGained);
    }

    // ---------- everybody ----------

    [Fact]
    public void Movers_ListsTheBiggestGainersClimbersAndNewTop100Wallets()
    {
        var oldWallets = new List<(string, decimal)>();
        for (var i = 0; i < 150; i++)
            oldWallets.Add(($"W{i:D3}", 10_000m - i * 10));
        var old = Snap(Now.AddHours(-24), oldWallets.ToArray());

        var nowWallets = oldWallets.ToList();
        nowWallets[120] = ("W120", 50_000m);          // jumps from rank 121 to rank 1, gains 41,200
        nowWallets[100] = ("W100", 9_500m);           // gains 500
        nowWallets.Add(("Newcomer", 20_000m));          // new, straight to the top
        var now = Snap(Now, nowWallets.ToArray());

        var movers = TimeMachine.Movers(now, old, 5);

        Assert.Equal("W120", movers.Gainers[0].Address);
        Assert.Equal(41_200m, movers.Gainers[0].PointsGained);
        Assert.Equal("Newcomer", movers.Gainers[1].Address);
        Assert.True(movers.Gainers[1].IsNew);
        Assert.Equal("W120", movers.Climbers[0].Address);
        Assert.Equal(120, movers.Climbers[0].OldRank - movers.Climbers[0].NewRank);
        Assert.DoesNotContain(movers.Climbers, c => c.Address == "Newcomer");   // a new wallet has no old rank to climb from
        Assert.Contains(movers.NewInTop100, m => m.Address == "W120" && !m.IsNew);   // came from rank 121
        Assert.Contains(movers.NewInTop100, m => m.Address == "Newcomer" && m.IsNew);
        Assert.Equal(151, movers.WalletsNow);
        Assert.Equal(150, movers.WalletsBefore);
    }

    [Fact]
    public void Movers_NeedsTwoSnapshotsAtLeastSixHoursApart()
    {
        Assert.True(TimeMachine.GetMovers(_store).NotEnoughData);

        _store.Save(Snap(Now.AddHours(-2), ("A", 1m)));
        _store.Save(Snap(Now, ("A", 2m)));
        Assert.True(TimeMachine.GetMovers(_store).NotEnoughData);

        _store.Save(Snap(Now.AddHours(-24), ("A", 1m)));
        Assert.False(TimeMachine.GetMovers(_store).NotEnoughData);
    }
}
