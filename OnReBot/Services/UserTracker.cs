namespace OnReBot.Services;

// Counts unique Telegram users. Stores only numeric user ids, one per line (no messages, no wallet addresses).
// The file is read once at startup; afterwards each message is a quick in-memory check and only a new user touches the disk.
public class UserTracker
{
    private readonly string _path;
    private readonly HashSet<long> _users = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public UserTracker(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (long.TryParse(line, out var id))
                    _users.Add(id);
            }
        }
    }

    public int Count
    {
        get { lock (_users) return _users.Count; }
    }

    public async Task RegisterAsync(long userId, CancellationToken ct)
    {
        bool isNew;
        lock (_users)
            isNew = _users.Add(userId);

        if (!isNew)
            return;

        await _fileLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_path, userId + Environment.NewLine, ct);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
