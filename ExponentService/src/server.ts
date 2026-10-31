import http from "node:http";
import { Vault, YtPosition, LOCAL_ENV } from "@exponent-labs/exponent-sdk";
import { Connection, PublicKey } from "@solana/web3.js";
import { getExtras, getOnReMarkets, timed, type OnReMarket } from "./extras.js";

const port = Number(process.env.EXPONENT_SERVICE_PORT ?? 3001);
const connection = new Connection(process.env.SOLANA_RPC_URL ?? "https://api.mainnet-beta.solana.com", "confirmed");
const REQUEST_TIMEOUT_MS = 20_000;

type ApiYtPosition = {
  vaultAddress: string;
  avgCostBasis: number;
  totalSizeLamports: number;
  totalRealizedPnlLamports: number;
  decimals: number;
  claimedInterestUsd: number;
};

// Official Exponent API (https://docs.exponent.finance/api-documentation). If it fails, only the PnL fields are left out.
// No caching: every request reads fresh data.
async function getApiPositions(wallet: string): Promise<Map<string, ApiYtPosition>> {
  try {
    const response = await fetch(`https://api.exponent.finance/positions/yt/user/${wallet}`, {
      signal: AbortSignal.timeout(10_000),
      headers: { "User-Agent": "OnReBot/1.0" },
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const items = (await response.json()) as ApiYtPosition[];
    return new Map(items.map((i) => [i.vaultAddress, i]));
  } catch (e) {
    console.error("Exponent API unavailable:", String(e));
    return new Map();
  }
}

// Current YT price (official /py-prices API). Checked against the Exponent app: it is effectively in USD.
// Shared data (key = vault address), cached briefly. Only successful prices are cached. ageMs is set when served from cache.
const PRICE_CACHE_MS = 30 * 1000;
const priceCache = new Map<string, { at: number; price: number }>();
type PriceEntry = { price: number; ageMs: number | null };

async function getYtPrices(vaultAddresses: string[]): Promise<Map<string, PriceEntry>> {
  const result = new Map<string, PriceEntry>();
  const missing: string[] = [];
  for (const address of vaultAddresses) {
    const cached = priceCache.get(address);
    if (cached && Date.now() - cached.at < PRICE_CACHE_MS) result.set(address, { price: cached.price, ageMs: Date.now() - cached.at });
    else missing.push(address);
  }
  if (missing.length === 0) return result;

  try {
    const response = await fetch("https://api.exponent.finance/py-prices", {
      method: "POST",
      signal: AbortSignal.timeout(10_000),
      headers: { "Content-Type": "application/json", "User-Agent": "OnReBot/1.0" },
      body: JSON.stringify({ vaultAddresses: missing }),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const items = (await response.json()) as { vaultAddress: string; ytPrice: number | null }[];
    for (const item of items) {
      if (item.ytPrice == null) continue;
      priceCache.set(item.vaultAddress, { at: Date.now(), price: item.ytPrice });
      result.set(item.vaultAddress, { price: item.ytPrice, ageMs: null });
    }
  } catch (e) {
    console.error("Exponent py-prices unavailable:", String(e));
  }
  return result;
}

// Vault state (exchange rate, maturity, mint decimals) is shared and changes slowly; cached because the public RPC is slow.
const VAULT_CACHE_MS = 5 * 60 * 1000;
const vaultCache = new Map<string, { at: number; vault: Vault; decimals: number }>();

async function getVault(address: string, maxAgeMs = VAULT_CACHE_MS) {
  const cached = vaultCache.get(address);
  if (cached && Date.now() - cached.at < maxAgeMs) return { ...cached, ageMs: Date.now() - cached.at };

  const vault = await Vault.load(LOCAL_ENV, connection, new PublicKey(address));
  const mint = await connection.getParsedAccountInfo(vault.mintYt);
  const decimals = (mint.value?.data as any)?.parsed?.info?.decimals ?? 9;
  vaultCache.set(address, { at: Date.now(), vault, decimals });
  return { vault, decimals, ageMs: null as number | null };
}

async function getPositions(wallet: string, requestedVaults: string[]) {
  const owner = new PublicKey(wallet);

  // OnRe markets are discovered from the API, so new markets show up without a config change. The requested vaults
  // (from the bot's config, e.g. matured markets no longer listed) are added on top. If discovery fails, use those alone.
  let discovered: OnReMarket[] = [];
  let discoveryFailed = false;
  try {
    discovered = (await timed("getOnReMarkets", () => getOnReMarkets())).markets;
  } catch (e) {
    console.error("Exponent markets unavailable:", String(e));
    discoveryFailed = true;
  }
  const vaultAddresses = [...new Set([...requestedVaults, ...discovered.map((m) => m.vault)])];
  const nameByVault = new Map(discovered.map((m) => [m.vault, m.name]));

  for (const address of vaultAddresses) knownVaults.add(address);

  // The Exponent API calls and the Solana RPC reads do not depend on each other, so they run at the same time.
  const apiWork = timed("api+prices+extras (parallel)", () =>
    Promise.all([getApiPositions(wallet), getYtPrices(vaultAddresses), getExtras(wallet, discovered)]),
  );
  const rpcWork = (async () => {
    const loaded = await timed("getVault x" + vaultAddresses.length, () => Promise.all(vaultAddresses.map((address) => getVault(address))));
    // One RPC call for all vaults instead of one per vault. A vault where the wallet never held YT gives null:
    // that means "no position", not an error.
    const loadedPositions = await timed("YtPosition.loadBatch (RPC)", () =>
      YtPosition.loadBatch(LOCAL_ENV, connection, owner, loaded.map((l) => l.vault)),
    );
    return { loaded, loadedPositions };
  })();
  const [[api, ytPrices, extras], { loaded, loadedPositions }] = await Promise.all([apiWork, rpcWork]);

  const results = await Promise.all(
    vaultAddresses.map(async (address, i) => {
      const { vault, decimals } = loaded[i];
      const position = loadedPositions[i];
      if (position == null) return null;
      const scale = 10 ** decimals;
      const yt = Number(position.ytBalance) / scale;
      const claimable = position.getClaimableInterest(vault.currentSyExchangeRate) / scale;
      const expired = vault.expirationDate.getTime() < Date.now();

      // only active positions: YT in a non-expired market, or claimable interest
      if (!((!expired && yt > 0) || claimable > 0)) return null;

      const stats = api.get(address);
      const priceEntry = expired ? undefined : ytPrices.get(address);
      const ytPrice = priceEntry?.price;
      // Age of the cached price (null = fetched fresh just now). The vault state is refreshed in the background
      // every few minutes and only moves the claimable-interest estimate negligibly, so it is not tagged.
      const cachedMs = priceEntry?.ageMs ?? 0;
      const cachedAgeSeconds = cachedMs > 0 ? Math.floor(cachedMs / 1000) : null;

      // Total PnL, computed the same way as the Exponent app (checked against a screenshot of a test wallet: -154.14 + 203.18 - 75.28 = -26.24):
      // Price appreciation = recorded traded size × (current price - average cost)
      //   Total = Price appreciation + Claimed yield + Realized PnL (+ Unclaimed yield)
      const realizedPnl = stats ? stats.totalRealizedPnlLamports / 10 ** stats.decimals : null;
      const priceAppreciation =
        ytPrice != null && stats ? (stats.totalSizeLamports / 10 ** stats.decimals) * (ytPrice - stats.avgCostBasis) : null;
      // 1 ONyc = currentSyExchangeRate USD (confirmed against Kamino). This part is not verified with real data because it was zero in testing.
      const unclaimedYieldUsd = claimable * vault.currentSyExchangeRate;
      const totalPnl =
        priceAppreciation != null && realizedPnl != null && stats
          ? priceAppreciation + stats.claimedInterestUsd + realizedPnl + unclaimedYieldUsd
          : null;

      return {
        vault: address,
        name: nameByVault.get(address) ?? null,
        yt,
        claimableInterest: claimable,
        expiresAt: vault.expirationDate.toISOString(),
        expired,
        avgCostBasis: stats?.avgCostBasis ?? null,
        currentValue: ytPrice != null ? yt * ytPrice : null,
        priceAppreciation,
        claimedInterestUsd: stats?.claimedInterestUsd ?? null,
        realizedPnl,
        unclaimedYieldUsd,
        totalPnl,
        cachedAgeSeconds,
      };
    }),
  );
  return {
    positions: results.filter((p) => p != null),
    tranches: extras.tranches,
    liquidity: extras.liquidity,
    extrasFailed: extras.failed || discoveryFailed,
    extrasCachedAgeSeconds: extras.cachedAgeMs != null ? Math.floor(extras.cachedAgeMs / 1000) : null,
  };
}

// Vault state used to be loaded (about 2 s or more on the public RPC) by whichever user came first after the cache
// expired, which with low traffic is nearly every report. Refreshing it in the background removes that wait.
const PREWARM_INTERVAL_MS = 4 * 60 * 1000; // shorter than VAULT_CACHE_MS so entries never expire between refreshes
const knownVaults = new Set<string>();

async function prewarm() {
  try {
    for (const market of (await getOnReMarkets()).markets) knownVaults.add(market.vault);
    await Promise.allSettled([...knownVaults].map((address) => getVault(address, PREWARM_INTERVAL_MS - 1000)));
  } catch (e) {
    console.error("Prewarm failed:", String(e));
  }
}

http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  const match = url.pathname.match(/^\/positions\/([1-9A-HJ-NP-Za-km-z]{32,44})$/);
  if (!match) {
    res.writeHead(404).end();
    return;
  }

  try {
    const vaults = url.searchParams.getAll("vault");
    const result = await Promise.race([
      getPositions(match[1], vaults),
      new Promise<never>((_, reject) => setTimeout(() => reject(new Error("timeout")), REQUEST_TIMEOUT_MS)),
    ]);
    res.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify(result));
  } catch (e) {
    console.error(e);
    res.writeHead(500, { "Content-Type": "application/json" }).end(JSON.stringify({ error: String(e) }));
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`exponent service on http://127.0.0.1:${port}`);
  void prewarm();
  setInterval(() => void prewarm(), PREWARM_INTERVAL_MS).unref();
});
