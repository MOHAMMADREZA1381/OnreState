using System.Collections.Concurrent;
using OnReBot;
using OnReBot.Clients;
using OnReBot.Config;
using OnReBot.Models;
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

var jupiter = new JupiterClient(http);
var titan = new TitanClient(http, config.TitanApiKey);

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
// The market overview is shared data that the Node service caches, so it is cheap; the limit only stops button spam.
const int MaxMarketsPerWindow = 5;
var marketsTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
var swapTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
// The calculator asks Jupiter for about five quotes per amount and the amount differs per user (so nothing can be shared), hence a tighter limit.
const int MaxCalcPerWindow = 3;
var calcTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
// Chats that are about to type an amount for the calculator (true = buy ONyc with USDC, false = sell ONyc).
var waitingForAmount = new ConcurrentDictionary<long, bool>();
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

    // Market overview: no wallet needed.
    if (update.CallbackQuery is { Data: ReportButtons.Markets, Message: { } marketsMessage } marketsCallback)
    {
        var marketsChatId = marketsMessage.Chat.Id;
        var marketsNow = DateTimeOffset.UtcNow;
        var marketsWait = TryTake(marketsTimes, marketsChatId, MaxMarketsPerWindow, marketsNow, out var marketsUses);
        if (marketsWait != null)
        {
            await client.AnswerCallbackQuery(marketsCallback.Id,
                $"Rate limit reached: 5 market requests every 5 minutes. Try again in {Math.Ceiling(marketsWait.Value.TotalSeconds)}s.",
                showAlert: true, cancellationToken: ct);
            return;
        }

        await client.AnswerCallbackQuery(marketsCallback.Id, cancellationToken: ct);
        await users.RegisterAsync(marketsCallback.From.Id, ct);
        _ = Task.Run(() => SendMarketsAsync(client, marketsChatId, marketsNow, marketsUses, ct), ct);
        return;
    }

    // Calculator: first the direction, then the user types an amount.
    if (update.CallbackQuery is { Data: ReportButtons.Calc, Message: { } calcMessage } calcCallback)
    {
        await client.AnswerCallbackQuery(calcCallback.Id, cancellationToken: ct);
        await users.RegisterAsync(calcCallback.From.Id, ct);
        await client.SendMessage(calcMessage.Chat.Id, "🧮 <b>Calculator</b>\n<blockquote>What do you want to convert?</blockquote>",
            parseMode: ParseMode.Html, replyMarkup: ReportButtons.CalcChoiceKeyboard(), cancellationToken: ct);
        return;
    }

    if (update.CallbackQuery is { Data: ReportButtons.CalcBuy or ReportButtons.CalcSell, Message: { } calcChoiceMessage } calcChoice)
    {
        var buy = calcChoice.Data == ReportButtons.CalcBuy;
        await client.AnswerCallbackQuery(calcChoice.Id, cancellationToken: ct);
        await users.RegisterAsync(calcChoice.From.Id, ct);
        await RemoveKeyboardAsync(client, calcChoiceMessage.Chat.Id, calcChoiceMessage.Id, ct);
        waitingForAddress.TryRemove(calcChoiceMessage.Chat.Id, out _);
        waitingForAmount[calcChoiceMessage.Chat.Id] = buy;
        await client.SendMessage(calcChoiceMessage.Chat.Id,
            buy ? "💵 Send the amount of <b>USDC</b> you want to convert to ONyc.\n<blockquote>For example: <code>100</code></blockquote>"
                : "🪙 Send the amount of <b>ONyc</b> you want to convert to USDC.\n<blockquote>For example: <code>50</code></blockquote>",
            parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    // Swap rates: no wallet needed.
    if (update.CallbackQuery is { Data: ReportButtons.Swap, Message: { } swapMessage } swapCallback)
    {
        var swapChatId = swapMessage.Chat.Id;
        var swapNow = DateTimeOffset.UtcNow;
        var swapWait = TryTake(swapTimes, swapChatId, MaxMarketsPerWindow, swapNow, out var swapUses);
        if (swapWait != null)
        {
            await client.AnswerCallbackQuery(swapCallback.Id,
                $"Rate limit reached: 5 swap-rate requests every 5 minutes. Try again in {Math.Ceiling(swapWait.Value.TotalSeconds)}s.",
                showAlert: true, cancellationToken: ct);
            return;
        }

        await client.AnswerCallbackQuery(swapCallback.Id, cancellationToken: ct);
        await users.RegisterAsync(swapCallback.From.Id, ct);
        _ = Task.Run(() => SendSwapRatesAsync(client, swapChatId, swapNow, swapUses, ct), ct);
        return;
    }

    if (update.CallbackQuery is { Data: ReportButtons.Rank, Message: { } callbackMessage } callback)
    {
        // Answer first: Telegram only accepts the answer for a short time.
        await client.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        await users.RegisterAsync(callback.From.Id, ct);
        waitingForAmount.TryRemove(callbackMessage.Chat.Id, out _);
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
        waitingForAmount.TryRemove(chatId, out _);
        var keyboard = ReportButtons.StartKeyboard();
        await client.SendMessage(chatId, "👋 <b>Welcome!</b>\n\nCheck your <b>OnRe rank</b>, <b>points</b> and <b>active positions</b> on <b>Kamino</b>, <b>Loopscale</b> and <b>Exponent</b>.\n\n<blockquote>Tap <b>🏆 My Rank</b> to check a wallet, or <b>📊 Markets</b> for the current APY and prices, <b>💱 Swap rate</b> for the best ONyc ⇄ USDC rate, or <b>🧮 Calculator</b> to see what an amount turns into.</blockquote>\n\n💙 <i>Support my content:</i> follow <a href=\"https://x.com/BoombasticZZ\">@BoombasticZZ</a> on X (Twitter).",
            parseMode: ParseMode.Html, replyMarkup: keyboard,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
        return;
    }

    // The amount for the calculator
    if (waitingForAmount.TryGetValue(chatId, out var calcBuy))
    {
        if (!SwapCalculator.TryParseAmount(text, out var calcAmount))
        {
            await client.SendMessage(chatId,
                $"❌ <b>Invalid amount</b>\n<blockquote>Send a number between 0 and {SwapCalculator.MaxAmount:N0}, for example <code>100</code>.</blockquote>",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var calcNow = DateTimeOffset.UtcNow;
        var calcWait = TryTake(calcTimes, chatId, MaxCalcPerWindow, calcNow, out var calcUses);
        if (calcWait != null)
        {
            await client.SendMessage(chatId,
                $"⏳ <b>Rate limit reached</b>\n<blockquote>3 calculations every 5 minutes. Try again in <b>{Math.Ceiling(calcWait.Value.TotalSeconds)}s</b>.</blockquote>",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        waitingForAmount.TryRemove(chatId, out _);
        _ = Task.Run(() => SendCalculatorAsync(client, chatId, calcBuy, calcAmount, calcNow, calcUses, ct), ct);
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

// Current APY, prices and maturity of the OnRe markets (shared data, no wallet).
Task SendMarketsAsync(ITelegramBotClient client, long chatId, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct) =>
    RunJobAsync(client, chatId, requestedAt, times, "⏳ <i>Fetching markets…</i>",
        async () =>
        {
            var overview = await portfolio.GetOverviewAsync(ct);
            return MessageFormatter.FormatMarkets(overview.Overview, overview.Failed, DateTimeOffset.UtcNow);
        },
        retryKeyboard: ReportButtons.MarketsKeyboard(), finalKeyboard: null, ct);

// Calculator: what the typed amount turns into on each DEX (Jupiter) and at Titan.
Task SendCalculatorAsync(ITelegramBotClient client, long chatId, bool buy, decimal amount, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct) =>
    RunJobAsync(client, chatId, requestedAt, times, "⏳ <i>Calculating…</i>",
        async () =>
        {
            var jupiterTask = jupiter.GetQuotesAsync(buy, amount, ct);
            var titanTask = titan.GetQuotesAsync(buy, amount, ct);
            await Task.WhenAll(jupiterTask, titanTask);
            var quotes = jupiterTask.Result.Concat(titanTask.Result).ToList();
            if (buy && await OnReMintRateAsync(amount, ct) is { } mint)
                quotes.Add(mint);
            return MessageFormatter.FormatCalculator(buy, amount, quotes);
        },
        retryKeyboard: ReportButtons.CalcChoiceKeyboard(), finalKeyboard: null, ct);

// Minting ONyc at OnRe's own price (NAV, from the Exponent overview); null when the NAV is not available.
async Task<SwapRate?> OnReMintRateAsync(decimal usdc, CancellationToken ct)
{
    var overview = await portfolio.GetOverviewAsync(ct);
    var nav = OnReMint.Nav(overview.Overview);
    return nav == null ? null : OnReMint.Rate(usdc, nav.Value, overview.Overview!.CachedAgeSeconds);
}

// ONyc <-> USDC swap rates from Jupiter (shared data, no wallet).
Task SendSwapRatesAsync(ITelegramBotClient client, long chatId, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct) =>
    RunJobAsync(client, chatId, requestedAt, times, "⏳ <i>Fetching swap rates…</i>",
        async () =>
        {
            // Jupiter and Titan are asked at the same time; Titan is skipped when no API key is set.
            var jupiterTask = jupiter.GetRatesAsync(ct);
            var titanTask = titan.GetRatesAsync(ct);
            await Task.WhenAll(jupiterTask, titanTask);
            var rates = jupiterTask.Result;
            rates.Rates.AddRange(titanTask.Result);
            if (await OnReMintRateAsync(JupiterClient.Amount, ct) is { } mint)
                rates.Rates.Add(mint);
            return MessageFormatter.FormatSwapRates(rates);
        },
        retryKeyboard: ReportButtons.SwapKeyboard(), finalKeyboard: null, ct);

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
    try
    {
        await client.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: replyMarkup, cancellationToken: ct);
    }
    catch (Exception ex) when (ex.Message.Contains("can't parse entities", StringComparison.OrdinalIgnoreCase))
    {
        // Safety net: a formatting mistake must not leave the user without a report
        Console.Error.WriteLine($"HTML rejected, sending plain text: {ex.Message}");
        await client.SendMessage(chatId, MessageFormatter.ToPlainText(text), replyMarkup: replyMarkup, cancellationToken: ct);
    }
}

Task HandleError(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
{
    Console.Error.WriteLine($"[{source}] {exception.Message}");
    return Task.CompletedTask;
}
