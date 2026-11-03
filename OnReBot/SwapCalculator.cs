using System.Globalization;

namespace OnReBot;

// Reads the amount a user types into the swap calculator.
public static class SwapCalculator
{
    public const decimal MaxAmount = 1_000_000m;
    public const int MaxDecimals = 6;

    // "100", "$100", "1,000.50", "100 usd" -> 100, 100, 1000.50, 100. Zero, negatives, huge amounts and text are rejected.
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim().TrimStart('$').Replace(",", "");
        foreach (var suffix in new[] { "usdc", "usd", "onyc" })
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length].TrimEnd();
                break;
            }
        }

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed <= 0 || parsed > MaxAmount)
            return false;
        if (decimal.Round(parsed, MaxDecimals) != parsed)
            return false;

        amount = parsed;
        return true;
    }
}
