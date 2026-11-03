using OnReBot.Models;

namespace OnReBot.Clients;

// Minting ONyc straight from OnRe (the "Deposit" box of app.onre.finance): USDC is exchanged for ONyc at OnRe's own price (the NAV)
// with no price impact, and OnRe charges no mint fee (only the Solana network fee). The NAV is the exchange rate of the ONyc market
// on Exponent, which matches the "Price NAV" shown in the OnRe app. Only buying is listed: redeeming ONyc is not an instant swap
// (it runs when liquidity is available), so it cannot be compared with a DEX price.
public static class OnReMint
{
    public const string Label = "OnRe mint";

    // The NAV in USD per ONyc, or null when it is not known (the overview is unavailable or has no ONyc market).
    public static decimal? Nav(ExponentOverview? overview)
    {
        var nav = overview?.Markets.FirstOrDefault(m => m.Name.StartsWith("ONyc ", StringComparison.Ordinal))?.TokenPrice;
        return nav is > 0 ? nav : null;
    }

    // What `usdc` buys when minted at the given NAV.
    public static SwapRate Rate(decimal usdc, decimal nav, int? cachedSeconds) =>
        new()
        {
            Buy = true,
            Dex = Label,
            AmountIn = usdc,
            AmountOut = usdc / nav,
            UsdPerOnyc = nav,
            PriceImpactPct = 0m,
            Routes = new() { Label },
            CachedSeconds = cachedSeconds
        };
}
