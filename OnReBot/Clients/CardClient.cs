using System.Net.Http.Json;
using System.Text.Json;
using OnReBot.Models;

namespace OnReBot.Clients;

// Asks the Node service to draw the share card (an image with rank, points and points per platform).
// Nothing is stored: the wallet and numbers are only used to draw the image.
public class CardClient(HttpClient http, string serviceUrl)
{
    private const int MaxRows = 5;

    public async Task<byte[]> RenderAsync(string wallet, OnReResponse onRe, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"{serviceUrl.TrimEnd('/')}/card", Build(wallet, onRe), new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // The request body of the Node service: points per platform, largest first. Small platforms beyond the row limit are summed as "Other".
    public static CardRequest Build(string wallet, OnReResponse onRe)
    {
        var b = onRe.PointsBreakdown;
        var all = new List<CardPlatform>();

        void Add(string name, decimal points)
        {
            if (points > 0)
                all.Add(new CardPlatform { Name = name, Points = points });
        }

        Add("Permissionless Boost", b.PermissionlessBoost);
        Add("Kamino", b.Kamino.Values.Sum());
        Add("Exponent", b.Exponent.Yt + b.Exponent.Lp);
        Add("Loopscale", b.Loopscale.Values.Sum());
        Add("Ratex", b.Ratex);
        Add("Carrot", b.Carrot);
        Add("Carrot Lending", b.CarrotLending);
        Add("Elemental", b.Elemental);
        Add("Exponent Vault", b.ExponentVault);
        Add("OnRe Yield+", b.OnreYieldPlus);
        Add("Orca", b.Orca.Values.Sum());
        Add("Referral bonus", b.ReferralBonus);

        var sorted = all.OrderByDescending(p => p.Points).ToList();
        var platforms = sorted.Take(sorted.Count > MaxRows ? MaxRows - 1 : MaxRows).ToList();
        if (sorted.Count > MaxRows)
            platforms.Add(new CardPlatform { Name = "Other", Points = sorted.Skip(MaxRows - 1).Sum(p => p.Points) });

        return new CardRequest { Wallet = wallet, Rank = onRe.Rank, TotalPoints = onRe.TotalPoints, Platforms = platforms };
    }
}

public class CardRequest
{
    public string Wallet { get; set; } = "";
    public int Rank { get; set; }
    public decimal TotalPoints { get; set; }
    public List<CardPlatform> Platforms { get; set; } = new();
}

public class CardPlatform
{
    public string Name { get; set; } = "";
    public decimal Points { get; set; }
}
