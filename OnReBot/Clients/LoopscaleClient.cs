using System.Net.Http.Json;
using System.Text.Json;
using OnReBot.Config;
using OnReBot.Models;

namespace OnReBot.Clients;

public class LoopscaleClient(HttpClient http, BotConfig config)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    // این endpoint فیلتر ولت ندارد و همهٔ سپرده‌گذاران vault را برمی‌گرداند؛ پس پاسخ هر vault (مشترک بین همهٔ کاربران) کوتاه‌مدت کش می‌شود.
    // کلید کش آدرس vault است، نه ولت کاربر. فقط پاسخ موفق کش می‌شود.
    private readonly Dictionary<string, (DateTimeOffset FetchedAt, Dictionary<string, long> Deposits)> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<LoopscalePosition>> GetPositionsAsync(string wallet, CancellationToken ct)
    {
        var positions = new List<LoopscalePosition>();
        foreach (var vault in config.LoopscaleVaults)
        {
            var (deposits, cachedFor) = await GetVaultDepositsAsync(vault.Address, ct);
            if (deposits.TryGetValue(wallet, out var amount) && amount > 0)
            {
                positions.Add(new LoopscalePosition
                {
                    VaultName = vault.Name,
                    Usdc = amount / 1_000_000m,
                    CachedSeconds = cachedFor == null ? null : (int)cachedFor.Value.TotalSeconds
                });
            }
        }
        return positions;
    }

    // CachedFor: null یعنی همین الان از Loopscale گرفته شد؛ در غیر این صورت سن داده‌ی کش.
    private async Task<(Dictionary<string, long> Deposits, TimeSpan? CachedFor)> GetVaultDepositsAsync(string vaultAddress, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(vaultAddress, out var cached))
            {
                var age = DateTimeOffset.UtcNow - cached.FetchedAt;
                if (age < CacheTtl)
                    return (cached.Deposits, age);
            }

            var body = new
            {
                vaultAddresses = new[] { vaultAddress },
                page = 0,
                pageSize = 20,
                sort = 0,
                sortDirection = 1
            };
            using var response = await http.PostAsJsonAsync("https://tars.loopscale.com/v1/markets/lending_vaults/deposits", body, ct);
            response.EnsureSuccessStatusCode();

            var deposits = ParseDeposits(await response.Content.ReadAsStringAsync(ct), vaultAddress);
            _cache[vaultAddress] = (DateTimeOffset.UtcNow, deposits);
            return (deposits, null);
        }
        finally
        {
            _lock.Release();
        }
    }

    // wallet -> amountSupplied (USDC با ۶ رقم اعشار) برای یک vault
    public static Dictionary<string, long> ParseDeposits(string json, string vaultAddress)
    {
        var vaults = JsonSerializer.Deserialize<List<LoopscaleVaultDeposits>>(json, Json.Options) ?? new();
        var result = new Dictionary<string, long>();
        foreach (var vault in vaults.Where(v => v.VaultAddress == vaultAddress))
        {
            foreach (var d in vault.UserDeposits)
            {
                result.TryGetValue(d.UserAddress, out var existing);
                result[d.UserAddress] = existing + d.AmountSupplied;
            }
        }
        return result;
    }
}
