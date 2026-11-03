using OnReBot.Models;

namespace OnReBot.Services;

public interface IExponentPositionProvider
{
    Task<ExponentResult> GetPositionsAsync(string wallet, CancellationToken ct);

    // Recent YT trades of the wallet.
    Task<ExponentHistory> GetHistoryAsync(string wallet, CancellationToken ct);

    // APY, prices and maturity of the active OnRe markets (not tied to a wallet).
    Task<ExponentOverview> GetOverviewAsync(CancellationToken ct);
}

// Used when no Exponent service is configured: it returns no Exponent positions.
public class NullExponentPositionProvider : IExponentPositionProvider
{
    public Task<ExponentResult> GetPositionsAsync(string wallet, CancellationToken ct) =>
        Task.FromResult(new ExponentResult());

    public Task<ExponentHistory> GetHistoryAsync(string wallet, CancellationToken ct) =>
        Task.FromResult(new ExponentHistory());

    public Task<ExponentOverview> GetOverviewAsync(CancellationToken ct) =>
        Task.FromResult(new ExponentOverview());
}
