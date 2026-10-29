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

var bot = new TelegramBotClient(token);
var waitingForAddress = new ConcurrentDictionary<long, bool>();
const int MaxReportsPerWindow = 2;
var reportWindow = TimeSpan.FromMinutes(5);
var reportTimes = new ConcurrentDictionary<long, List<DateTimeOffset>>();
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

async Task HandleUpdate(ITelegramBotClient client, Update update, CancellationToken ct)
{
    if (update.CallbackQuery is { Data: "rank", Message: { } callbackMessage } callback)
    {
        await users.RegisterAsync(callback.From.Id, ct);
        await client.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
        waitingForAddress[callbackMessage.Chat.Id] = true;
        await client.SendMessage(callbackMessage.Chat.Id, "📮 Send me your <b>Solana wallet address</b>.", parseMode: ParseMode.Html, cancellationToken: ct);
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
            await client.SendMessage(chatId, $"👥 <b>Total users:</b> {users.Count:N0}", parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    if (text.StartsWith("/start"))
    {
        var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🏆 My Rank", "rank"));
        await client.SendMessage(chatId, "👋 <b>Welcome!</b>\nCheck your OnRe rank, points, and your active positions on Kamino, Loopscale and Exponent.\n\nTap the button below to start.\n\n💙 Support my content: follow <a href=\"https://x.com/BoombasticZZ\">@BoombasticZZ</a> on X (Twitter).",
            parseMode: ParseMode.Html, replyMarkup: keyboard,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
        return;
    }

    if (!waitingForAddress.ContainsKey(chatId))
    {
        await client.SendMessage(chatId, "Send /start to begin.", parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    var wallet = text.Trim();
    if (!AddressValidator.IsValid(wallet))
    {
        await client.SendMessage(chatId, "❌ <b>Invalid wallet address.</b>\nSend a valid Solana address (base58, 32–44 characters).",
            parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }

    var now = DateTimeOffset.UtcNow;
    var times = reportTimes.GetOrAdd(chatId, _ => new List<DateTimeOffset>());
    TimeSpan? wait = null;
    lock (times)
    {
        times.RemoveAll(t => now - t >= reportWindow);
        if (times.Count >= MaxReportsPerWindow)
            wait = reportWindow - (now - times[0]);
        else
            times.Add(now);
    }
    if (wait != null)
    {
        await client.SendMessage(chatId,
            $"⏳ <b>Rate limit reached.</b>\nYou can request 2 reports every 5 minutes. Try again in {Math.Ceiling(wait.Value.TotalSeconds)}s.",
            parseMode: ParseMode.Html, cancellationToken: ct);
        return;
    }
    waitingForAddress.TryRemove(chatId, out _);

    // The polling loop awaits this handler, so a slow report must not run here: it would block every other user.
    _ = Task.Run(() => SendReportAsync(client, chatId, wallet, now, times, ct), ct);
}

// At most MaxConcurrentReports run at once (protects the small server's RAM). Extra requests wait a little, then get a "busy" reply.
async Task SendReportAsync(ITelegramBotClient client, long chatId, string wallet, DateTimeOffset requestedAt, List<DateTimeOffset> times, CancellationToken ct)
{
    var gotSlot = false;
    try
    {
        gotSlot = await reportSlots.WaitAsync(BusyAfter, ct);
        if (!gotSlot)
        {
            // The report never ran, so it must not count against the user's limit; let them resend the address.
            lock (times) times.Remove(requestedAt);
            waitingForAddress[chatId] = true;
            await client.SendMessage(chatId, "🚦 <b>The bot is busy right now.</b>\nPlease send your address again in a few seconds.",
                parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var report = await portfolio.GetReportAsync(wallet, ct);
        reportSlots.Release();
        gotSlot = false;
        await client.SendMessage(chatId, MessageFormatter.Format(report), parseMode: ParseMode.Html, cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"Report failed: {ex.GetType().Name}: {ex.Message}");
        lock (times) times.Remove(requestedAt);
        try
        {
            await client.SendMessage(chatId, "⚠️ <b>Something went wrong.</b>\nPlease try again in a moment.", parseMode: ParseMode.Html, cancellationToken: ct);
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

Task HandleError(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
{
    Console.Error.WriteLine($"[{source}] {exception.Message}");
    return Task.CompletedTask;
}
