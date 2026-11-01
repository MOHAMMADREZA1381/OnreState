using System.Collections.Concurrent;
using OnReBot;
using OnReBot.Clients;
using OnReBot.Config;
using OnReBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("The BOT_TOKEN environment variable is not set.");
    return 1;
}

var config = BotConfig.Load();

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("OnReBot/1.0 (telegram rank bot)");

var portfolio = new PortfolioService(
    new OnReClient(http),
    new KaminoClient(http, config),
    new LoopscaleClient(http, config),
    config.ExponentServiceUrl == ""
        ? new NullExponentPositionProvider()
        // The local Node service talks to the (sometimes slow) Solana RPC, so it gets a longer timeout than external APIs.
        : new HttpExponentPositionProvider(new HttpClient { Timeout = TimeSpan.FromSeconds(25) }, config));

// Stored outside the project folder so rebuilds never touch it.
var usersFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OnReBot", "users.txt");
var users = new UserTracker(usersFile);
Console.WriteLine($"Users file: {usersFile} ({users.Count} users)");

// The library long-polls Telegram for 100 s, so its HttpClient timeout must be longer than that (default 100 s collides
// with it and logs a "request timed out" on every quiet period). Connections are also recycled regularly.
var telegramHttp = new HttpClient(new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(10),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
})
{
    Timeout = TimeSpan.FromSeconds(130)
};
var bot = new TelegramBotClient(token, telegramHttp);
var waitingForAddress = new ConcurrentDictionary<long, bool>();
const int MaxReportsPerWindow = 2;
var reportWindow = TimeSpan.FromMinutes(5);
var reportTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
// The quick summary is a single cheap request, so it gets a more generous limit than the full report.
const int MaxSummariesPerWindow = 10;
var summaryTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
// History needs a few extra requests to Kamino and Exponent, so it has its own limit.
const int MaxHistoryPerWindow = 3;
var historyTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
const int MaxConcurrentReports = 5;
var BusyAfter = TimeSpan.FromSeconds(15);
var reportSlots = new SemaphoreSlim(MaxConcurrentReports, MaxConcurrentReports);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

bot.StartReceiving(HandleUpdate, HandleError, new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery] }, cts.Token);

var me = await bot.GetMe(cts.Token);
Console.WriteLine($"Bot @{me.Username} is running. Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
return 0;

// The polling loop awaits this handler, so it must return immediately: one hung call to Telegram (up to the HTTP timeout)
// would otherwise freeze the bot for every user. Each update is processed on its own.
Task HandleUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
{
    _ = Task.Run(async () =>
    {
        try
        {
            await ProcessUpdate(client, update, ct);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Update {update.Id} failed: {ex.GetType().Name}: {ex.Message}");
        }
    });
    return Task.CompletedTask;
}

async Task ProcessUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
{
    // Step 2: the user tapped "Full report" under the quick summary.
    if (update.CallbackQuery is { Message: { } fullMessage } fullCallback && ReportButtons.TryParseFull(fullCallback.Data, out var fullWallet))
    {
        var fullChatId = fullMessage.Chat.Id;
        var now = DateTimeOffset.UtcNow;
        var fullWait = TryTake(reportTimes, fullChatId, MaxReportsPerWindow, now, out var times);
        if (fullWait != null)
        {
            await client.AnswerCallbackQuery(fullCallback.Id,
                $"Rate limit reached: 2 full reports every 5 minutes. Try again in {Math.Ceiling(fullWait.Value.TotalSeconds)}s.",
                showAlert: true, cancellationToken: ct);
            return;
        }

        // Answer first: Telegram only accepts the answer for a short time.
        await client.AnswerCallbackQuery(fullCallback.Id, cancellationToken: ct);
        await users.RegisterAsync(fullCallback.From.Id, ct);
        await RemoveKeyboardAsync(client, fullChatId, fullMessage.Id, ct);

        // The polling loop awaits this handler, so a slow report must not run here: it would block every other user.
        _ = Task.Run(() => SendReportAsync(client, fullChatId, fullWallet, now, times, ct), ct);
        return;
    }

    // Step 3: the user tapped "History" under the positions.
    if (update.CallbackQuery is { Message: { } historyMessage } historyCallback && ReportButtons.TryParseHistory(historyCallback.Data, out var historyWallet))
    {
        var historyChatId = historyMessage.Chat.Id;
        var historyNow = DateTimeOffset.UtcNow;
        var historyWait = TryTake(historyTimes, historyChatId, MaxHistoryPerWindow, historyNow, out var historyUses);
        if (historyWait != null)
        {
            await client.AnswerCallbackQuery(historyCallback.Id,
                $"Rate limit reached: 3 history requests every 5 minutes. Try again in {Math.Ceiling(historyWait.Value.TotalSeconds)}s.",
                showAlert: true, cancellationToken: ct);
            return;
        }

        await client.AnswerCallbackQuery(historyCallback.Id, cancellationToken: ct);
        await users.RegisterAsync(historyCallback.From.Id, ct);
        await RemoveKeyboardAsync(client, historyChatId, historyMessage.Id, ct);
        _ = Task.Run(() => SendHistoryAsync(client, historyChatId, historyWallet, historyNow, historyUses, ct), ct);
        return;
    }

    // "No thanks": just remove the buttons.
    if (update.CallbackQuery is { Data: ReportButtons.Decline, Message: { } declineMessage } declineCallback)
    {
        await client.AnswerCallbackQuery(declineCallback.Id, cancellationToken: ct);
        await RemoveKeyboardAsync(client, declineMessage.Chat.Id, declineMessage.Id, ct);
        return;
    }

    if (update.CallbackQuery is { Data: "rank", Message: { } callbackMessage } callback)
    {
        // Answer first: Telegram only accepts the answer for a short time.
        await client.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        await users.RegisterAsync(callback.From.Id, ct);
        waitingForAddress[callbackMessage.Chat.Id] = true;
        await client.SendMessage(callbackMessage.Chat.Id, "📮 Send me your <b>Solana wallet address</b>.\n<blockquote>A base58 address, 32–44 characters.</blockquote>", parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    if (update.Message is not { Text: { } text } message)
        return;

    var chatId = message.Chat.Id;

    if (message.From != null)
        await users.RegisterAsync(message.From.Id, ct);

    // Admin only; everyone else gets no reply.
    if (text.StartsWith("/stats"))
    {
        if (config.AdminUserId != 0 && message.From?.Id == config.AdminUserId)
            await client.SendMessage(chatId, $"👥 <b>Total users:</b> <code>{users.Count:N0}</code>", parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    if (text.StartsWith("/start"))
    {
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🏆 My Rank", "rank"));
        await client.SendMessage(chatId, "👋 <b>Welcome!</b>\n\nCheck your <b>OnRe rank</b>, <b>points</b> and <b>active positions</b> on <b>Kamino</b>, <b>Loopscale</b> and <b>Exponent</b>.\n\n<blockquote>Tap <b>🏆 My Rank</b> below to start.</blockquote>\n\n💙 <i>Support my content:</i> follow <a href=\"https://x.com/BoombasticZZ\">@BoombasticZZ</a> on X (Twitter).",
            parseMode: ParseMode.Html, replyMarkup: keyboard,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
        return;
    }

    if (!waitingForAddress.ContainsKey(chatId))
    {
        await client.SendMessage(chatId, "👋 Send <code>/start</code> to begin.", parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    var wallet = text.Trim();
    if (!AddressValidator.IsValid(wallet))
    {
        await client.SendMessage(chatId, "❌ <b>Invalid wallet address</b>\n<blockquote>Send a valid Solana address (base58, 32–44 characters).</blockquote>",
            parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    var wait = TryTake(summaryTimes, chatId, MaxSummariesPerWindow, DateTimeOffset.UtcNow, out _);
    if (wait != null)
    {
        await client.SendMessage(chatId,
            $"⏳ <b>Rate limit reached</b>\n<blockquote>Try again in <b>{Math.Ceiling(wait.Value.TotalSeconds)}s</b>.</blockquote>",
            parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }
    waitingForAddress.TryRemove(chatId, out _);

    // Step 1: rank and points right away, then ask whether the user wants the positions too.
    var summary = await portfolio.GetSummaryAsync(wallet, ct);
    await client.SendMessage(chatId, MessageFormatter.FitToTelegram(MessageFormatter.FormatSummary(summary.OnRe, summary.Failed)),
        parseMode: ParseMode.Html, replyMarkup: ReportButtons.Keyboard(wallet), cancellationToken: ct);
}

// Sliding window per user. Returns null (and records the use) when allowed, otherwise how long the user has to wait.
TimeSpan? TryTake(ConcurrentDictionary<long, List<DateTimeOffset>> store, long key, int max, DateTimeOffset now, out List<DateTimeOffset> times)
{
    times = store.GetOrAdd(key, _ => new List<DateTimeOffset>());
    lock (times)
    {
        times.RemoveAll(t => now - t >= reportWindow);
        if (times.Count >= max)
            return reportWindow - (now - times[0]);
        times.Add(now);
        return null;
    }
}

// Removes the buttons from a message (so a second tap cannot start another report). Failures are not important.
async Task RemoveKeyboardAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken ct)
{
    try
    {
        await client.EditMessageReplyMarkup(chatId, messageId, replyMarkup: null, cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Could not remove buttons: {ex.Message}");
    }
}

// Step 2: the positions of the wallet.
Task SendReportAsync(ITelegramBotClient client, long chatId, string wallet, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct) =>
    RunJobAsync(client, chatId, requestedAt, times, "⏳ <i>Fetching your report…</i>",
        async () => MessageFormatter.FormatPositions(await portfolio.GetReportAsync(wallet, ct)), // rank and points were already shown in step 1
        retryKeyboard: ReportButtons.Keyboard(wallet), finalKeyboard: ReportButtons.HistoryKeyboard(wallet), ct);

// Step 3: recent changes from the platforms' own APIs.
Task SendHistoryAsync(ITelegramBotClient client, long chatId, string wallet, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct) =>
    RunJobAsync(client, chatId, requestedAt, times, "⏳ <i>Fetching history…</i>",
        async () => MessageFormatter.FormatHistory(await portfolio.GetHistoryAsync(wallet, ct)),
        retryKeyboard: ReportButtons.HistoryKeyboard(wallet), finalKeyboard: null, ct);

// At most MaxConcurrentReports jobs run at once (protects the small server's RAM). Extra requests wait a little, then get a "busy" reply.
// The user sees a placeholder right away, which is replaced by the result. A job that never ran or failed does not count against the
// user's limit, and the retry buttons come back so they can simply tap again.
async Task RunJobAsync(ITelegramBotClient client, long chatId, DateTimeOffset requestedAt, List<DateTimeOffset> times, string placeholderText,
    Func<Task<string>> buildText, InlineKeyboardMarkup retryKeyboard, InlineKeyboardMarkup? finalKeyboard, CancellationToken ct)
{
    var gotSlot = false;
    int? placeholderId = null;
    try
    {
        gotSlot = await reportSlots.WaitAsync(BusyAfter, ct);
        if (!gotSlot)
        {
            lock (times) times.Remove(requestedAt);
            await client.SendMessage(chatId, "🚦 <b>The bot is busy right now</b>\n<i>Please tap the button again in a few seconds.</i>",
                parseMode: ParseMode.Html, replyMarkup: retryKeyboard, cancellationToken: ct);
            return;
        }

        // Immediate feedback: this can take a few seconds, so say so right away and replace the message when the result is ready.
        placeholderId = await TrySendAsync(client, chatId, placeholderText, ct);

        var text = await buildText();
        reportSlots.Release();
        gotSlot = false;
        await ShowAsync(client, chatId, placeholderId, text, finalKeyboard, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Job failed: {ex.GetType().Name}: {ex.Message}");
        lock (times) times.Remove(requestedAt);
        try
        {
            await ShowAsync(client, chatId, placeholderId, "⚠️ <b>Something went wrong</b>\n<i>Please try again in a moment.</i>", retryKeyboard, ct);
        }
        catch (Exception sendEx)
        {
            Console.Error.WriteLine($"Could not send error message: {sendEx.Message}");
        }
    }
    finally
    {
        if (gotSlot)
            reportSlots.Release();
    }
}

// Sends a message and returns its id, or null if sending failed (the report is then delivered as a normal message instead).
async Task<int?> TrySendAsync(ITelegramBotClient client, long chatId, string text, CancellationToken ct)
{
    try
    {
        return (await client.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct)).Id;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Could not send placeholder: {ex.Message}");
        return null;
    }
}

// Replaces the placeholder with the final text; if that is not possible, sends the text as a new message.
async Task ShowAsync(ITelegramBotClient client, long chatId, int? messageId, string text, InlineKeyboardMarkup? replyMarkup, CancellationToken ct)
{
    text = MessageFormatter.FitToTelegram(text);
    if (messageId != null)
    {
        try
        {
            await client.EditMessageText(chatId, messageId.Value, text, parseMode: ParseMode.Html, replyMarkup: replyMarkup, cancellationToken: ct);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Could not edit message, sending a new one: {ex.Message}");
        }
    }
    await client.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: replyMarkup, cancellationToken: ct);
}

Task HandleError(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
{
    Console.Error.WriteLine($"[{source}] {exception.Message}");
    return Task.CompletedTask;
}
