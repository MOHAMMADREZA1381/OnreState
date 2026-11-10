# OnReBot

A Telegram bot that shows the **OnRe rank and points** of a Solana wallet, plus its **active positions** on Kamino, Loopscale and Exponent (including Exponent PnL).

Send `/start`, tap **🏆 My Rank**, then send a Solana wallet address. **📊 Markets** shows the current APY and prices of the OnRe markets, and **💱 Swap rate** the best ONyc ⇄ USDC swap rate, both without a wallet.

## How it works (step by step)

1. `/start`, then **🏆 My Rank**, then you send your wallet address.
2. The bot answers right away with your **rank and points** (one fast request) and asks whether you want the positions too: **✅ Full report** or **❌ No thanks**. The question notes that all requests are sent from a single IP address, so some data may be temporarily unavailable. More buttons: **🏅 My position** (your place on the OnRe leaderboard, see below), **📈 My momentum** (how fast you gain points, see "Time Machine" below) and **🖼 Share card**, an image of your rank, total points and points per platform. The rank line also says how far up you are, for example `Top 5.5% of 25,079`.
3. If you tap **Full report**, a second message follows with your **active positions** (Kamino, Loopscale, Exponent). Tapping the button also removes it, so one tap starts exactly one report.

4. Under the positions there is a **📈 History** button. It shows recent changes taken from the platforms' own APIs:
   - **Kamino**: net value of your obligation over the available daily snapshots (for example `$11.6048 → $11.6148`).
   - **Exponent**: your YT trades per market with totals (how many buys and sells, YT and USD bought and sold, the net YT and the **net spent**: USD paid for buys minus USD received from sells, because "bought" also counts purchases paid with the proceeds of earlier sales) and a table of the trades themselves (full date, YT amount with a `+` for bought and `-` for sold, price in USD per YT and the value of the trade in USD; up to 30 per market, newest first). The API reports amounts in SY, which are USD-denominated (1 SY is worth $1; checked against the token movements of real transactions on-chain), so no exchange rate is applied.
   - **OnRe (rank and points) and Loopscale** do not offer history through their APIs, so nothing is shown for them.

**🏅 Leaderboard** (from `/start`, no wallet needed) shows the top 10 of OnRe's own leaderboard (the list in the OnRe app: shortened address and total points) and how many wallets have points. **🏅 My position** (under the quick summary) shows where your wallet stands: your rank, the share of wallets you are ahead of (`Top 5.5%`), the two wallets above and below you, how many points you need to pass the wallet just above you and to reach the next round ranks (#1,000, #500, #100, #50, #10). It all comes from OnRe's public leaderboard API (`rewards.api.onre.finance/api/v1/points/leaderboard`, a few pages of 50 wallets, cached for 5 minutes and shared by all users); nothing is stored. The points of the wallets move all the time, so the gaps do too (the message says so). The Share card shows the same `Top x%` under the rank.

**🧭 How the top 50 earn** (the button under the leaderboard) adds up the points of the 50 highest-ranked wallets per platform: a bar and percentage for each platform and how many of the 50 earn points there (for example `Kamino ████░░░░░░ 42.8% 40/50`). The leaderboard API lists every wallet's points per platform, and for 49 of the top 50 wallets the platform points add up to the wallet's total to the point (the OnRe API itself is off for one of them), so the shares are shares of the sum of the platform points. Exponent points include the Protected (senior and junior tranche) points; "Wallet" is the points OnRe gives for the wallet itself. It uses the same cached first page as the leaderboard and stores nothing.

**⏳ Time Machine.** OnRe's API has no history, so the bot builds it: once an hour it saves a **snapshot of OnRe's public leaderboard** (rank, address and points of every wallet that has points, the same list the OnRe app shows, plus OnRe's NAV, APY and AUM at that moment). It reads 2,000 wallets per request, about 13 requests one second apart, which took 22 seconds and produced a 1 MB gzip file in a real run. Snapshots are kept as small files next to the users file (every snapshot for 48 hours, then one per day for 30 days, about 50 MB). With two snapshots a day apart the bot can show:

- **📈 My momentum** (under the quick summary): the points you gained in the last 24 hours (and the last 7 days once a week of snapshots exists), your points per day, how many ranks you climbed or fell, and how many days the next round ranks (#1,000, #500, #100, #50, #10) are away *at the pace of the last day*. Everyone keeps earning points, so that is a rough guide, and the message says so. A comparison needs two snapshots at least 6 hours apart; until then the bot says how long to wait.
- **📈 My Progress Chart** (under the quick summary): a shareable PNG with mint Points and muted orange Rank on a dark teal background. It shows today and the preceding six UTC dates, using the latest readable Time Machine snapshot for each date. Shading marks dates without saved rank and points for the wallet, and lines connect consecutive recorded dates. With no recorded dates, the bot sends a short explanation. The chart labels the shortened wallet address and dates the earliest and latest values used in the comparison. Points changes include a percentage when the first value is positive. Better ranks appear higher.
- **🚀 Movers** (button under the leaderboard): the wallets that gained the most points and climbed the most ranks in 24 hours (among the top 5,000), and who is new in the top 100.

**What is stored:** these snapshots contain only OnRe's *public* leaderboard (the same data anyone can read in the OnRe app), nothing about the bot's own users. The bot still stores no wallet address that a user typed in; it only keeps the numeric Telegram user ids it counts (see below).

**🖼 Share card** (under the quick summary) sends a PNG with rank, total points and the points per platform (the biggest four plus "Other"). Both image features shorten the wallet address to its first 6 and last 4 characters and use the local Node service to render PNGs with [resvg](https://github.com/yisibl/resvg-js) and bundled DejaVu Sans fonts. A short-lived worker renders one image at a time. Share cards and progress charts share a limit of 2 images per chat every 5 minutes.

**📊 Markets** (from `/start`, no wallet needed) starts with OnRe's official live numbers (NAV, APY and AUM from `core.api.onre.finance/data/live-nav`, `live-apy` and `live-tvl`, the same ones the OnRe app shows) and then shows, for each active OnRe market on Exponent: token price, implied APY, underlying APY, YT and PT price and the days to maturity, plus the senior/junior APY of the Protected tranche. It is the same for every user, so the Node service caches it for 5 minutes and the message says how old the data is when it is cached.

**💱 Swap rate** (no wallet needed) shows what 1 ONyc costs to buy and what it sells for on each DEX, one line per DEX with the best price marked ✅, plus the best route of each aggregator (which can mix several DEXes). The DEX prices come from [Jupiter](https://jup.ag)'s public quote API (no key), which can be limited to one DEX per request. If `TITAN_API_KEY` is set, [Titan](https://titan.exchange)'s Developers Portal API is asked as well (its free plan allows 1 request per second, so its two requests are spaced out). Prices are based on a swap of 100 USDC or 100 ONyc. Buying is also compared with **minting at OnRe** itself (the Deposit box of the OnRe app): USDC becomes ONyc at OnRe's own price (the NAV: OnRe's official live number, the "Price NAV" of the OnRe app; if it cannot be read, the exchange rate of the ONyc market on Exponent is used, which matches it) with no price impact and no mint fee, only the Solana network fee. Redeeming ONyc with OnRe is not an instant swap (it runs when liquidity is available), so it is not listed and the message says why. A DEX with no direct ONyc/USDC pool is left out. Only quotes are requested, nothing is executed. Quotes are cached for 20 seconds and shared by all users (the message says so when they are cached).

**🧮 Calculator** answers "what does my amount turn into?". You pick a direction (USDC → ONyc or ONyc → USDC), type an amount (for example `100`, `$100` or `1,000.50`; at most 1,000,000 and 6 decimals) and get the result on each DEX and from each aggregator, best first, plus a summary of the best one: what you pay, what you get, the price per ONyc, the price impact (Jupiter only) and the minimum you receive if the price moves 0.5% against you. The quoted amounts already include the DEX pool fees and the price impact of your trade (that is how Jupiter and Titan quotes work); the Solana network fee (a tiny amount of SOL) is not included, and the message says so. Quotes only, nothing is executed. Each user may run 3 calculations every 5 minutes, because every amount needs its own quotes.

The wallet address travels inside the buttons themselves (Telegram callback data), so the bot does not need to remember anything between the steps. Nothing is stored for the history either: it is read from the platforms each time.

## What it shows

- Rank and total points from the OnRe rewards API, with a per-platform breakdown (only non-zero platforms).
- Active positions:
  - **Kamino**: deposits and debt in the configured markets.
  - **Loopscale**: lend-vault deposits.
  - **Exponent**, in the same three sections as the Exponent app:
    - **Farm**: YT balance, maturity, claimable interest, current value, average cost, and Total PnL (price appreciation + claimed yield + realized PnL).
    - **Protected**: senior/junior tranche positions (balance, value, APY, PnL).
    - **Liquidity**: CLMM LP positions (APY range and in-range status, value, estimated PnL).
  - OnRe markets on Exponent (ONyc and srONyc) are discovered automatically from the Exponent API, so new markets appear without a config change.
- PT positions are not covered yet.
- If one platform is down, only that section says "currently unavailable"; the rest of the report still works.

## Project layout

| Folder | What it is |
|---|---|
| `OnReBot/` | The bot (.NET 8) |
| `OnReBot.Tests/` | xUnit tests with placeholder wallet addresses |
| `ExponentService/` | Small Node/TypeScript service that reads Exponent positions and PnL |
| `deploy/` | systemd units and an Ubuntu deployment guide |

## Quick start (local)

Requirements: .NET 8 SDK and Node.js 20+ (tested with Node 24).

1. Create a bot with [@BotFather](https://t.me/BotFather) and copy the token.
2. Start the Exponent service:

   ```bash
   cd ExponentService
   npm ci
   npm run build
   npm start
   ```

   Use the built service for share cards and progress charts so the image worker is available.

3. In another terminal at the repository root, enter the **test bot token** at the hidden prompt. The bot reads it from `BOT_TOKEN` in the current process environment. Keep the token out of chat, command history, files, and Git.

   PowerShell:

   ```powershell
   $env:ONRE_DATA_DIR = Join-Path $PWD 'local-test-data'
   try {
       $env:BOT_TOKEN = Read-Host 'Test bot token' -MaskInput
       dotnet run --project OnReBot
   } finally {
       Remove-Item Env:BOT_TOKEN, Env:ONRE_DATA_DIR -ErrorAction SilentlyContinue
   }
   ```

   bash:

   ```bash
   (
     read -rsp 'Test bot token: ' BOT_TOKEN || exit 1
     printf '\n'
     export BOT_TOKEN
     export ONRE_DATA_DIR="$PWD/local-test-data"
     dotnet run --project OnReBot
   )
   ```

The PowerShell prompt requires PowerShell 7.2 or newer. For a chart smoke test, run `/start`, choose **My Rank**, send a wallet address, and tap **📈 My Progress Chart**. Use a wallet from recent public snapshots. To reuse existing history, copy the recent `*.snap.gz` files into `local-test-data/snapshots/` before starting the test bot. The test bot uses this separate folder for its snapshots and user count. With an empty history, the hourly job starts its first capture shortly after startup.

The Exponent service is optional: set `ExponentServiceUrl` to an empty string in `appsettings.json` to disable Exponent positions, Share card, and My Progress Chart rendering.

## Environment variables

None of these are stored in the repository.

| Name | Description |
|---|---|
| `BOT_TOKEN` | Bot token from BotFather (required) |
| `ONRE_DATA_DIR` | Optional data directory override for the user count and snapshots. Use a dedicated directory for a test bot. The default is the bot's existing `OnReBot` application data folder. |
| `TITAN_API_KEY` | Free API key from the [Titan developer platform](https://developer.titan.exchange) (optional). Adds Titan to the swap prices; without it only Jupiter is shown. Never put it in a file in the repository |
| `ADMIN_USER_ID` | Numeric Telegram id of the admin. Only this user gets a reply to `/stats` (optional; if unset, `/stats` is disabled) |
| `SOLANA_RPC_URL` | Solana RPC used by the Exponent service (optional; a private RPC such as Helius or QuickNode is faster and avoids rate limits) |
| `EXPONENT_SERVICE_PORT` | Port of the Exponent service (optional, default `3001`) |
| `EXPONENT_TIMING` | Set to `1` to make the Exponent service log how long each step takes (optional, for finding slow spots) |

## Configuration (`OnReBot/Config/appsettings.json`)

| Key | Description |
|---|---|
| `KaminoMarkets` | Array of `{Name, Address}`. Add an entry to track another market. Only the OnRe Market is enabled by default (PT-ONyc markets do not earn points). |
| `KaminoReserves` | Known reserves as `{Address, Symbol, Decimals}`. Token amounts are shown only for these reserves. |
| `LoopscaleVaults` | Array of `{Name, Address}` for Lend vaults. |
| `ExponentMarkets` | Optional array of `{Name, Vault}`: extra Exponent vaults to check for YT positions, for example matured markets that the API no longer lists. Active OnRe markets are discovered automatically. |
| `ExponentServiceUrl` | Address of the Exponent service (default `http://127.0.0.1:3001`). Empty disables Exponent positions and image rendering. |

## Behavior

- The wallet address is validated before any request (base58, 32 to 44 characters).
- Rate limit: at most 2 full reports per user every 5 minutes, and at most 10 quick summaries (rank and points) every 5 minutes and at most 3 history requests every 5 minutes. At most 5 reports run at the same time; extra requests wait briefly and then get a "busy" reply.
- Speed: a typical report (no tranche position) takes about 1 to 1.5 seconds. Exponent vault state is refreshed in the background so the first user after a quiet period does not wait for it, and all positions are read from the Solana RPC with a single call. The bot answers immediately with a "Fetching your report…" message and replaces it with the report. The slowest step is Exponent's tranche stats endpoint (3 to 6 seconds), which is only called for wallets that hold a Protected (tranche) position.
- Every HTTP request has a 10 second timeout (the local Exponent service gets 25 seconds because it talks to the Solana RPC).
- Caching is intentionally minimal. Only data shared by all users is cached, briefly: the Loopscale depositor list (30 s), the Exponent YT price (30 s), and the Exponent vault state and market lists (5 min). Anything served from a cache is tagged in the message (`🕒 cached data · 12s old`). Wallet-specific data (OnRe, Kamino, your Exponent position and PnL) is always fetched fresh.
- Exponent PnL comes from the [official Exponent API](https://docs.exponent.finance/api-documentation). Total PnL = price appreciation + claimed yield + realized PnL (+ unclaimed yield), the same breakdown the Exponent app uses. It is an estimate and can differ by a few cents because prices move. If the API is unavailable, the position is still shown with `PnL: temporarily unavailable`, and the Protected/Liquidity sections say they are currently unavailable. The Liquidity PnL is an estimate (value minus cost basis plus farm rewards), because the API does not define one.
- No attempt is made to bypass any bot protection (for example the Vercel Security Checkpoint on the Exponent app).
- The bot stores only numeric Telegram user ids (one per line) to count unique users. No messages and no wallet addresses typed by users are stored. Besides that it keeps the hourly snapshots of OnRe's public leaderboard described under "Time Machine" (public data, nothing about its users).

## Commands

| Command | Who | Description |
|---|---|---|
| `/start` | everyone | Welcome message with the **My Rank** and **Markets** buttons |
| `/stats` | admin only | Total number of unique users (no reply for anyone else) |

## Deployment

A step-by-step guide for an Ubuntu server (systemd, `/opt/onrebot`) is in [`deploy/DEPLOY.md`](deploy/DEPLOY.md).

For the progress chart release, first build both components and test the Telegram flow with a separate bot and recent snapshots. Check a wallet with multiple days, a gap, and a wallet with no recorded days. Also check Share card because it uses the same renderer.

After approval to merge and deploy, record the current production revision and back up the deployed bot and renderer together. Retain the production snapshot directory and use the existing server-managed `BOT_TOKEN`. Build both components from the approved revision, check the package with `deploy.ps1 -DryRun`, then deploy both components together. Confirm both services are active and verify My Rank, My Progress Chart, and Share card in Telegram. If verification fails, restore the previous bot and renderer build together and restart both services while keeping the snapshots.

## Tests

```bash
dotnet build
dotnet test
```

Sample data is in `OnReBot.Tests/TestData` and uses placeholder addresses. The tests do not call any live API.
