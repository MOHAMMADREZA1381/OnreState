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
    public const string Decline = "no";

    public static string FullData(string wallet) => FullPrefix + wallet;

    public static string HistoryData(string wallet) => HistoryPrefix + wallet;

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

    // Step 2 question under the quick summary
    public static InlineKeyboardMarkup Keyboard(string wallet) =>
        new(new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Full report", FullData(wallet)),
            InlineKeyboardButton.WithCallbackData("❌ No thanks", Decline),
        });

    // Step 3 button under the positions
    public static InlineKeyboardMarkup HistoryKeyboard(string wallet) =>
        new(new[] { InlineKeyboardButton.WithCallbackData("📈 History", HistoryData(wallet)) });
}
