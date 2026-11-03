using OnReBot.Services;
using Telegram.Bot.Types.ReplyMarkups;

namespace OnReBot;

// The buttons of the step-by-step flow. The wallet address travels inside the button's own data, so the bot does not
// have to remember anything between the steps. Telegram allows at most 64 bytes of callback data:
// "full:" / "hist:" (5) + a 44-character address = 49.
public static class ReportButtons
{
    public const string FullPrefix = "full:";
    public const string HistoryPrefix = "hist:";
    public const string CardPrefix = "card:";
    public const string Decline = "no";
    public const string Rank = "rank";
    public const string Markets = "markets";
    public const string Swap = "swap";
    public const string Calc = "calc";
    public const string CalcBuy = "calc:buy";
    public const string CalcSell = "calc:sell";

    public static string FullData(string wallet) => FullPrefix + wallet;

    public static string HistoryData(string wallet) => HistoryPrefix + wallet;

    public static string CardData(string wallet) => CardPrefix + wallet;

    public static bool TryParseCard(string? data, out string wallet) => TryParse(CardPrefix, data, out wallet);

    public static bool TryParseFull(string? data, out string wallet) => TryParse(FullPrefix, data, out wallet);

    public static bool TryParseHistory(string? data, out string wallet) => TryParse(HistoryPrefix, data, out wallet);

    private static bool TryParse(string prefix, string? data, out string wallet)
    {
        wallet = "";
        if (data == null || !data.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var candidate = data[prefix.Length..];
        if (!AddressValidator.IsValid(candidate))
            return false;

        wallet = candidate;
        return true;
    }

    // The two entry points under the welcome message
    public static InlineKeyboardMarkup StartKeyboard() =>
        new(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("🏆 My Rank", Rank) },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Markets", Markets),
                InlineKeyboardButton.WithCallbackData("💱 Swap rate", Swap),
            },
            new[] { InlineKeyboardButton.WithCallbackData("🧮 Calculator", Calc) },
        });

    // Which way the calculator should convert
    public static InlineKeyboardMarkup CalcChoiceKeyboard() =>
        new(new[]
        {
            InlineKeyboardButton.WithCallbackData("💵 USDC → ONyc", CalcBuy),
            InlineKeyboardButton.WithCallbackData("🪙 ONyc → USDC", CalcSell),
        });

    // Retry button for the market overview
    public static InlineKeyboardMarkup MarketsKeyboard() =>
        new(new[] { InlineKeyboardButton.WithCallbackData("📊 Markets", Markets) });

    // Retry button for the swap rates
    public static InlineKeyboardMarkup SwapKeyboard() =>
        new(new[] { InlineKeyboardButton.WithCallbackData("💱 Swap rate", Swap) });

    // Step 2 question under the quick summary
    public static InlineKeyboardMarkup Keyboard(string wallet) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Full report", FullData(wallet)),
                InlineKeyboardButton.WithCallbackData("❌ No thanks", Decline),
            },
            new[] { InlineKeyboardButton.WithCallbackData("🖼 Share card", CardData(wallet)) },
        });

    // Step 3 button under the positions
    public static InlineKeyboardMarkup HistoryKeyboard(string wallet) =>
        new(new[] { InlineKeyboardButton.WithCallbackData("📈 History", HistoryData(wallet)) });
}
