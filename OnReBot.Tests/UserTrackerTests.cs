using OnReBot.Services;

namespace OnReBot.Tests;

public class UserTrackerTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"onrebot-users-{Guid.NewGuid():N}", "users.txt");

    [Fact]
    public async Task CountsEachUserOnce()
    {
        var tracker = new UserTracker(TempFile());

        await tracker.RegisterAsync(1, default);
        await tracker.RegisterAsync(2, default);
        await tracker.RegisterAsync(1, default);

        Assert.Equal(2, tracker.Count);
    }

    [Fact]
    public async Task SurvivesRestart()
    {
        var path = TempFile();
        var first = new UserTracker(path);
        await first.RegisterAsync(10, default);
        await first.RegisterAsync(20, default);

        var afterRestart = new UserTracker(path);

        Assert.Equal(2, afterRestart.Count);
        await afterRestart.RegisterAsync(20, default);   // already known
        await afterRestart.RegisterAsync(30, default);
        Assert.Equal(3, afterRestart.Count);
        Assert.Equal(3, File.ReadAllLines(path).Length);  // only new users touch the file
    }

    [Fact]
    public async Task StoresOnlyNumericIds()
    {
        var path = TempFile();
        var tracker = new UserTracker(path);
        await tracker.RegisterAsync(123456789, default);

        Assert.Equal(new[] { "123456789" }, File.ReadAllLines(path));
    }
}
