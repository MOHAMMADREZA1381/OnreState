using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Services;

public class PortfolioService(
    OnReClient onRe,
    KaminoClient kamino,
    LoopscaleClient loopscale,
    IExponentPositionProvider exponent)
{
    // Step 1 of the report: only rank and points (a single fast request to OnRe).
    public async Task<(OnReResponse? OnRe, bool Failed)> GetSummaryAsync(string wallet, CancellationToken ct)
    {
        var result = await Safe(() => onRe.GetPointsAsync(wallet, ct));
        return (result.Value, result.Failed);
    }

    // Recent changes straight from the platforms' own APIs (nothing is stored by the bot). Only Kamino and Exponent offer history.
    public async Task<HistoryReport> GetHistoryAsync(string wallet, CancellationToken ct)
    {
        var kaminoTask = Safe(() => kamino.GetHistoryAsync(wallet, ct));
        var exponentTask = Safe(() => exponent.GetHistoryAsync(wallet, ct));
        await Task.WhenAll(kaminoTask, exponentTask);

        var exponentHistory = exponentTask.Result.Value ?? new ExponentHistory();
        if (exponentTask.Result.Failed)
            exponentHistory.Failed = true;

        return new HistoryReport
        {
            Kamino = kaminoTask.Result.Value ?? new(),
            KaminoFailed = kaminoTask.Result.Failed,
            Exponent = exponentHistory
        };
    }

    // The top of the OnRe leaderboard (shared data, no wallet).
    // The first page (50 wallets) is shared by the leaderboard and the points mix, so both use the same cached request.
    public async Task<(LeaderboardPage? Page, bool Failed)> GetTopAsync(int size, CancellationToken ct)
    {
        var result = await Safe(() => onRe.GetLeaderboardPageAsync(0, OnReClient.PageSize, ct));
        if (result.Failed || result.Value == null)
            return (null, true);

        var page = result.Value;
        return (new LeaderboardPage { Content = page.Content.Take(size).ToList(), TotalElements = page.TotalElements, CachedSeconds = page.CachedSeconds }, false);
    }

    // How many wallets have points, for the "Top x%" figure. null when it could not be read: the figure is then simply left out.
    // It is only a nice extra next to the rank, so it gets at most 2 seconds and can never slow the quick summary down.
    public async Task<long?> GetTotalUsersAsync(CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        var result = await Safe(() => onRe.GetTotalUsersAsync(limit.Token));
        return result.Failed ? null : result.Value;
    }

    // OnRe's official NAV, APY and AUM (each can be null when its request failed).
    public Task<OnReStats> GetOnReStatsAsync(CancellationToken ct) => onRe.GetStatsAsync(ct);

    // Where a wallet stands on the leaderboard. NoPoints: the wallet has none (OnRe still returns a rank for unknown wallets).
    public async Task<(LeaderboardPosition? Position, bool NoPoints, bool Failed)> GetPositionAsync(string wallet, CancellationToken ct)
    {
        var me = await Safe(() => onRe.GetPointsAsync(wallet, ct));
        if (me.Failed || me.Value == null)
            return (null, false, true);
        if (me.Value.TotalPoints <= 0)
            return (null, true, false);

        var position = await Safe(() => onRe.GetPositionAsync(wallet, me.Value, ct));
        return (position.Value, false, position.Failed);
    }

    // Market overview (shared data, no wallet).
    public async Task<(ExponentOverview? Overview, bool Failed)> GetOverviewAsync(CancellationToken ct)
    {
        var result = await Safe(() => exponent.GetOverviewAsync(ct));
        return (result.Value, result.Failed);
    }

    public async Task<PortfolioReport> GetReportAsync(string wallet, CancellationToken ct)
    {
        var onReTask = Safe(() => onRe.GetPointsAsync(wallet, ct));
        var kaminoTask = Safe(() => kamino.GetPositionsAsync(wallet, ct));
        var loopscaleTask = Safe(() => loopscale.GetPositionsAsync(wallet, ct));
        var exponentTask = Safe(() => exponent.GetPositionsAsync(wallet, ct));
        await Task.WhenAll(onReTask, kaminoTask, loopscaleTask, exponentTask);

        return new PortfolioReport
        {
            OnRe = onReTask.Result.Value,
            OnReFailed = onReTask.Result.Failed,
            Kamino = kaminoTask.Result.Value ?? new(),
            KaminoFailed = kaminoTask.Result.Failed,
            Loopscale = loopscaleTask.Result.Value ?? new(),
            LoopscaleFailed = loopscaleTask.Result.Failed,
            Exponent = exponentTask.Result.Value?.Positions ?? new(),
            ExponentFailed = exponentTask.Result.Failed,
            ExponentExtrasFailed = exponentTask.Result.Value?.ExtrasFailed ?? false
        };
    }

    private static async Task<(T? Value, bool Failed)> Safe<T>(Func<Task<T>> action)
    {
        try
        {
            return (await action(), false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{typeof(T).Name}] {ex.GetType().Name}: {ex.Message}");
            return (default, true);
        }
    }
}
