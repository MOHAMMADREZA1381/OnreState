using System.Globalization;
using System.IO.Compression;
using System.Text;
using OnReBot.Models;

namespace OnReBot.Services;

// Keeps the snapshots of OnRe's public leaderboard as small gzip text files, one per snapshot (rank, address, points per line).
// No database. Retention: every snapshot of the last 48 hours, then one per day for 30 days. That is about 50 MB of disk.
public class SnapshotStore
{
    private const string Extension = ".snap.gz";
    private const string TimeFormat = "yyyyMMdd'T'HHmm'Z'";
    private static readonly TimeSpan KeepAll = TimeSpan.FromHours(48);
    private static readonly TimeSpan KeepDaily = TimeSpan.FromDays(30);

    private readonly string _dir;
    private readonly object _lock = new();
    // A few recently loaded snapshots stay in memory (about 3 MB each), because every request compares two of them.
    private readonly Dictionary<DateTimeOffset, Snapshot> _cache = new();
    private readonly List<DateTimeOffset> _cacheOrder = new();
    private const int CacheSize = 4;

    public SnapshotStore(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
    }

    // ---------- writing ----------

    public void Save(Snapshot snapshot)
    {
        var final = PathFor(snapshot.At);
        var temp = final + ".tmp";

        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            writer.WriteLine($"v1,{snapshot.At.ToUnixTimeSeconds()},{Text(snapshot.Nav)},{Text(snapshot.Apy)},{Text(snapshot.Aum)}");
            foreach (var e in snapshot.Entries)
                writer.WriteLine($"{e.Rank},{e.Address},{e.Points.ToString(CultureInfo.InvariantCulture)}");
        }

        // The file appears only when it is complete, so a crash never leaves a half-written snapshot behind
        File.Move(temp, final, overwrite: true);
        lock (_lock)
            Remember(snapshot);
    }

    // ---------- reading ----------

    // The times of all snapshots on disk, oldest first
    public List<DateTimeOffset> List()
    {
        var times = new List<DateTimeOffset>();
        foreach (var path in Directory.GetFiles(_dir, "*" + Extension))
        {
            var name = Path.GetFileName(path);
            var stamp = name[..^Extension.Length];
            if (DateTimeOffset.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
                times.Add(at);
        }
        times.Sort();
        return times;
    }

    public DateTimeOffset? LatestTime() => List() is { Count: > 0 } all ? all[^1] : null;

    public Snapshot? Load(DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(at, out var hit))
                return hit;
        }

        var path = PathFor(at);
        if (!File.Exists(path))
            return null;

        Snapshot snapshot;
        try
        {
            snapshot = Parse(File.OpenRead(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException)
        {
            Console.Error.WriteLine($"[Snapshots] Could not read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }

        lock (_lock)
            Remember(snapshot);
        return snapshot;
    }

    public Snapshot? Latest() => LatestTime() is { } at ? Load(at) : null;

    public static Snapshot Parse(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        var header = (reader.ReadLine() ?? "").Split(',');
        if (header.Length < 5 || header[0] != "v1")
            throw new FormatException("unknown snapshot header");

        var snapshot = new Snapshot
        {
            At = DateTimeOffset.FromUnixTimeSeconds(long.Parse(header[1], CultureInfo.InvariantCulture)),
            Nav = Number(header[2]),
            Apy = Number(header[3]),
            Aum = Number(header[4])
        };

        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(',');
            if (parts.Length != 3)
                continue;
            snapshot.Entries.Add(new SnapshotEntry(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                parts[1],
                decimal.Parse(parts[2], CultureInfo.InvariantCulture)));
        }
        return snapshot;
    }

    // ---------- retention ----------

    // Deletes what is no longer needed: older than 48 hours keeps only the first snapshot of each day, and nothing older than 30 days stays.
    public int Prune(DateTimeOffset now)
    {
        var all = List();
        var keepDays = new HashSet<DateTime>();
        var deleted = 0;

        foreach (var at in all)   // oldest first, so the first snapshot of a day is the one kept
        {
            var age = now - at;
            var keep = age <= KeepAll || (age <= KeepDaily && keepDays.Add(at.UtcDateTime.Date));
            if (keep)
                continue;

            try
            {
                File.Delete(PathFor(at));
                deleted++;
                lock (_lock)
                    Forget(at);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[Snapshots] Could not delete {at:u}: {ex.Message}");
            }
        }
        return deleted;
    }

    // ---------- helpers ----------

    private string PathFor(DateTimeOffset at) => Path.Combine(_dir, at.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture) + Extension);

    private static string Text(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private static decimal? Number(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private void Remember(Snapshot snapshot)
    {
        _cache[snapshot.At] = snapshot;
        _cacheOrder.Remove(snapshot.At);
        _cacheOrder.Add(snapshot.At);
        while (_cacheOrder.Count > CacheSize)
        {
            _cache.Remove(_cacheOrder[0]);
            _cacheOrder.RemoveAt(0);
        }
    }

    private void Forget(DateTimeOffset at)
    {
        _cache.Remove(at);
        _cacheOrder.Remove(at);
    }
}
