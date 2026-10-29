using OnReBot.Models;

namespace OnReBot.Services;

public interface IExponentPositionProvider
{
    Task<List<ExponentPosition>> GetPositionsAsync(string wallet, CancellationToken ct);
}

// فاز ۱: پوزیشن Exponent فعلاً خالی است. فاز ۲ با سرویس Node/TS پر می‌شود.
public class NullExponentPositionProvider : IExponentPositionProvider
{
    public Task<List<ExponentPosition>> GetPositionsAsync(string wallet, CancellationToken ct) =>
        Task.FromResult(new List<ExponentPosition>());
}
