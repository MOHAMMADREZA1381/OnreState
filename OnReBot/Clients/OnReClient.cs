using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

public class OnReClient(HttpClient http)
{
    public async Task<OnReResponse> GetPointsAsync(string wallet, CancellationToken ct)
    {
        using var response = await http.GetAsync($"https://rewards.api.onre.finance/api/v1/points/{wallet}", ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public static OnReResponse Parse(string json) =>
        JsonSerializer.Deserialize<OnReResponse>(json, Json.Options)!;
}
