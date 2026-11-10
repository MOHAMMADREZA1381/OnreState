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
    public const string ProgressPrefix = "progress:";
    public const string Decline = "no";
    public const string Rank = "rank";
    public const string Markets = "markets";
    public const string Swap = "swap";
    public const string Calc = "calc";
    public const string Board = "board";
    public const string Mix = "mix";
    public const string Movers = "movers";
    public const string MomentumPrefix = "mom:";
    public const string PositionPrefix = "pos:";
    public const string CalcBuy = "calc:buy";
    public const string CalcSell = "calc:sell";

    public static string FullData(string wallet) => FullPrefix + wallet;

    public static string HistoryData(string wallet) => HistoryPrefix + wallet;

    public static string CardData(string wallet) => CardPrefix + wallet;
    public static string ProgressData(string wallet) => ProgressPrefix + wallet;
    public static bool TryParseProgress(string? data, out string wallet) => TryParse(ProgressPrefix, data, out wallet);

    public static string MomentumData(string wallet) => MomentumPrefix + wallet;

    public static bool TryParseMomentum(string? data, out string wallet) => TryParse(MomentumPrefix, data, out wallet);

    public static string PositionData(string wallet) => PositionPrefix + wallet;

    public static bool TryParsePosition(string? data, out string wallet) => TryParse(PositionPrefix, data, out wallet);

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
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏆 My Rank", Rank),
                InlineKeyboardButton.WithCallbackData("🏅 Leaderboard", Board),
            },
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

    // Under the leaderboard: how the top wallets earn their points, and who moves the most
    public static InlineKeyboardMarkup BoardFinalKeyboard() =>
        new(new[]
        {
            InlineKeyboardButton.WithCallbackData("🧭 How the top 50 earn", Mix),
            InlineKeyboardButton.WithCallbackData("🚀 Movers", Movers),
        });

    public static InlineKeyboardMarkup MoversKeyboard() =>
        new(new[] { InlineKeyboardButton.WithCallbackData("🚀 Movers", Movers) });

    public static InlineKeyboardMarkup MomentumKeyboard(string wallet) =>
        new(new[] { InlineKeyboardButton.WithCallbackData("📈 My momentum", MomentumData(wallet)) });

    public static InlineKeyboardMarkup MixKeyboard() =>
        new(new[] { InlineKeyboardButton.WithCallbackData("🧭 How the top 50 earn", Mix) });

    // Retry buttons for the leaderboard screens
    public static InlineKeyboardMarkup BoardKeyboard() =>
        new(new[] { InlineKeyboardButton.WithCallbackData("🏅 Leaderboard", Board) });

    public static InlineKeyboardMarkup PositionKeyboard(string wallet) =>
        new(new[] { InlineKeyboardButton.WithCallbackData("🏅 My position", PositionData(wallet)) });

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
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏅 My position", PositionData(wallet)),
                InlineKeyboardButton.WithCallbackData("📈 My momentum", MomentumData(wallet)),
            },
            new[] { InlineKeyboardButton.WithCallbackData("🖼 Share card", CardData(wallet)) },
            new[] { InlineKeyboardButton.WithCallbackData("📈 My Progress Chart", ProgressData(wallet)) },
        });

    // Step 3 button under the positions
    public static InlineKeyboardMarkup HistoryKeyboard(string wallet) =>
        new(new[] { InlineKeyboardButton.WithCallbackData("📈 History", HistoryData(wallet)) });
}
