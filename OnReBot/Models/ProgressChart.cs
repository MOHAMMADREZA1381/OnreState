namespace OnReBot.Models;

public record ProgressChart(string Wallet, IReadOnlyList<ProgressDay> Days)
{
    public bool HasData => Days.Any(day => day.Rank.HasValue);
}

public record ProgressDay(DateOnly Date, decimal? Points, int? Rank);
