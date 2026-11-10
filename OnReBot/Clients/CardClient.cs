using System.Net.Http.Json;
using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

// Asks the Node service to draw share cards and Time Machine charts.
// Nothing is stored: the wallet and numbers are only used to draw the image.
public class CardClient(HttpClient http, string serviceUrl)
{
    private const int MaxRows = 5;

    public Task<byte[]> RenderAsync(string wallet, OnReResponse onRe, long? totalUsers, CancellationToken ct) =>
        PostImageAsync("card", Build(wallet, onRe, totalUsers), ct);

    public Task<byte[]> RenderProgressAsync(ProgressChart chart, CancellationToken ct) =>
        PostImageAsync("progress-chart", new { chart.Wallet, chart.Days }, ct);

    private async Task<byte[]> PostImageAsync<T>(string route, T data, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"{serviceUrl.TrimEnd('/')}/{route}", data, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // The request body of the Node service: points per platform, largest first. Small platforms beyond the row limit are summed as "Other".
    public static CardRequest Build(string wallet, OnReResponse onRe, long? totalUsers = null)
    {
        var all = onRe.PointsBreakdown.Platforms().Where(p => p.Name != "Wallet").Select(p => new CardPlatform { Name = p.Name, Points = p.Points }).ToList();


        var sorted = all.OrderByDescending(p => p.Points).ToList();
        var platforms = sorted.Take(sorted.Count > MaxRows ? MaxRows - 1 : MaxRows).ToList();
        if (sorted.Count > MaxRows)
            platforms.Add(new CardPlatform { Name = "Other", Points = sorted.Skip(MaxRows - 1).Sum(p => p.Points) });

        return new CardRequest { Wallet = wallet, Rank = onRe.Rank, TotalPoints = onRe.TotalPoints, TotalUsers = totalUsers, Platforms = platforms };
    }
}

public class CardRequest
{
    public string Wallet { get; set; } = "";
    public int Rank { get; set; }
    public decimal TotalPoints { get; set; }

    // How many wallets have points (for "Top x%"); left out when unknown
    public long? TotalUsers { get; set; }
    public List<CardPlatform> Platforms { get; set; } = new();
}

public class CardPlatform
{
    public string Name { get; set; } = "";
    public decimal Points { get; set; }
}
