using OnReBot.Models;

namespace OnReBot.Services;

public interface IExponentPositionProvider
{
    Task<ExponentResult> GetPositionsAsync(string wallet, CancellationToken ct);
}

// Used when no Exponent service is configured: it returns no Exponent positions.
public class NullExponentPositionProvider : IExponentPositionProvider
{
    public Task<ExponentResult> GetPositionsAsync(string wallet, CancellationToken ct) =>
        Task.FromResult(new ExponentResult());
}
