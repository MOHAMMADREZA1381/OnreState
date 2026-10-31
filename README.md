# OnReBot

A Telegram bot that shows the **OnRe rank and points** of a Solana wallet, plus its **active positions** on Kamino, Loopscale and Exponent (including Exponent PnL).

Send `/start`, tap **🏆 My Rank**, then send a Solana wallet address.

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

   For development you can use `npm run dev` (runs the TypeScript directly).

3. In another terminal, start the bot with the token in an environment variable:

   PowerShell:

   ```powershell
   $env:BOT_TOKEN = "123456:ABC..."
   dotnet run --project OnReBot
   ```

   bash:

   ```bash
   export BOT_TOKEN="123456:ABC..."
   dotnet run --project OnReBot
   ```

The Exponent service is optional: set `ExponentServiceUrl` to an empty string in `appsettings.json` to run the bot without it (Exponent positions are then hidden).

## Environment variables

None of these are stored in the repository.

| Name | Description |
|---|---|
| `BOT_TOKEN` | Bot token from BotFather (required) |
| `ADMIN_USER_ID` | Numeric Telegram id of the admin. Only this user gets a reply to `/stats` (optional; if unset, `/stats` is disabled) |
| `SOLANA_RPC_URL` | Solana RPC used by the Exponent service (optional; a private RPC such as Helius or QuickNode is faster and avoids rate limits) |
| `EXPONENT_SERVICE_PORT` | Port of the Exponent service (optional, default `3001`) |
| `EXPONENT_TIMING` | Set to `1` to make the Exponent service log how long each step takes (optional, for finding slow spots) |

For local runs you can keep these in a file named `run-local.ps1`; that name is listed in `.gitignore`.

## Configuration (`OnReBot/Config/appsettings.json`)

| Key | Description |
|---|---|
| `KaminoMarkets` | Array of `{Name, Address}`. Add an entry to track another market. Only the OnRe Market is enabled by default (PT-ONyc markets do not earn points). |
| `KaminoReserves` | Known reserves as `{Address, Symbol, Decimals}`. Token amounts are shown only for these reserves. |
| `LoopscaleVaults` | Array of `{Name, Address}` for Lend vaults. |
| `ExponentMarkets` | Optional array of `{Name, Vault}`: extra Exponent vaults to check for YT positions, for example matured markets that the API no longer lists. Active OnRe markets are discovered automatically. |
| `ExponentServiceUrl` | Address of the Exponent service (default `http://127.0.0.1:3001`). Empty disables Exponent positions. |

## Behavior

- The wallet address is validated before any request (base58, 32 to 44 characters).
- Rate limit: at most 2 reports per user every 5 minutes. At most 5 reports run at the same time; extra requests wait briefly and then get a "busy" reply.
- Speed: a typical report (no tranche position) takes about 1 to 1.5 seconds. Exponent vault state is refreshed in the background so the first user after a quiet period does not wait for it, and all positions are read from the Solana RPC with a single call. The bot answers immediately with a "Fetching your report…" message and replaces it with the report. The slowest step is Exponent's tranche stats endpoint (3 to 6 seconds), which is only called for wallets that hold a Protected (tranche) position.
- Every HTTP request has a 10 second timeout (the local Exponent service gets 25 seconds because it talks to the Solana RPC).
- Caching is intentionally minimal. Only data shared by all users is cached, briefly: the Loopscale depositor list (30 s), the Exponent YT price (30 s), and the Exponent vault state and market lists (5 min). Anything served from a cache is tagged in the message (`🕒 cached data · 12s old`). Wallet-specific data (OnRe, Kamino, your Exponent position and PnL) is always fetched fresh.
- Exponent PnL comes from the [official Exponent API](https://docs.exponent.finance/api-documentation). Total PnL = price appreciation + claimed yield + realized PnL (+ unclaimed yield), the same breakdown the Exponent app uses. It is an estimate and can differ by a few cents because prices move. If the API is unavailable, the position is still shown with `PnL: temporarily unavailable`, and the Protected/Liquidity sections say they are currently unavailable. The Liquidity PnL is an estimate (value minus cost basis plus farm rewards), because the API does not define one.
- No attempt is made to bypass any bot protection (for example the Vercel Security Checkpoint on the Exponent app).
- The bot stores only numeric Telegram user ids (one per line) to count unique users. No messages and no wallet addresses are stored.

## Commands

| Command | Who | Description |
|---|---|---|
| `/start` | everyone | Welcome message with the **My Rank** button |
| `/stats` | admin only | Total number of unique users (no reply for anyone else) |

## Deployment

A step-by-step guide for an Ubuntu server (systemd, `/opt/onrebot`) is in [`deploy/DEPLOY.md`](deploy/DEPLOY.md).

## Tests

```bash
dotnet build
dotnet test
```

Sample data is in `OnReBot.Tests/TestData` and uses placeholder addresses. The tests do not call any live API.
