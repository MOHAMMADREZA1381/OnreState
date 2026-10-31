using OnReBot.Clients;
using OnReBot.Models;

namespace OnReBot.Services;

public class PortfolioService(
    OnReClient onRe,
    KaminoClient kamino,
    LoopscaleClient loopscale,
    IExponentPositionProvider exponent)
{
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
