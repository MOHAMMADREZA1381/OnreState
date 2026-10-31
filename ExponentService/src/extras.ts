// Exponent positions beyond the YT farm: Protected (tranche) and Liquidity (CLMM LP), plus discovery of OnRe markets.
// Data comes from the official Exponent API (https://docs.exponent.finance/api-documentation).

const API = "https://api.exponent.finance";
const HEADERS = { "User-Agent": "OnReBot/1.0" };

async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(API + path, { headers: HEADERS, signal: AbortSignal.timeout(10_000) });
  if (!response.ok) throw new Error(`HTTP ${response.status} for ${path.split("?")[0].replace(/\/[1-9A-HJ-NP-Za-km-z]{32,44}/g, "/…")}`);
  return (await response.json()) as T;
}

// Market lists are shared by every user and change slowly, so they are cached briefly. Failures are never cached.
const SHARED_TTL_MS = 5 * 60 * 1000;
const shared = new Map<string, { at: number; value: unknown }>();

async function getShared<T>(path: string): Promise<{ value: T; ageMs: number | null }> {
  const hit = shared.get(path);
  if (hit && Date.now() - hit.at < SHARED_TTL_MS) return { value: hit.value as T, ageMs: Date.now() - hit.at };
  const value = await apiGet<T>(path);
  shared.set(path, { at: Date.now(), value });
  return { value, ageMs: null };
}

// ---------- OnRe market discovery ----------

type ApiMarket = {
  vaultAddress: string;
  platformName: string;
  tokenName: string;
  marketStatus: string;
  maturityDateUnixTs: number;
  impliedApy: number | null;
  decimals: number;
};

export type OnReMarket = { vault: string; name: string; status: string; impliedApy: number | null; decimals: number };

// 1799586000 -> "10JAN27" (same style as the Exponent app)
function formatMaturity(unixSeconds: number): string {
  const d = new Date(unixSeconds * 1000);
  const month = d.toLocaleString("en-US", { month: "short", timeZone: "UTC" }).toUpperCase();
  return `${String(d.getUTCDate()).padStart(2, "0")}${month}${String(d.getUTCFullYear()).slice(2)}`;
}

export async function getOnReMarkets(): Promise<{ markets: OnReMarket[]; ageMs: number | null }> {
  const { value, ageMs } = await getShared<ApiMarket[]>("/markets");
  const markets = value
    .filter((m) => m.platformName === "OnRe")
    .map((m) => ({
      vault: m.vaultAddress,
      name: `${m.tokenName} ${formatMaturity(m.maturityDateUnixTs)}`,
      status: m.marketStatus,
      impliedApy: m.impliedApy,
      decimals: m.decimals ?? 9,
    }));
  return { markets, ageMs };
}

// ---------- Protected (tranche) and Liquidity (CLMM LP) ----------

type TrancheMarket = { address: string; platformName: string; baseTokenSymbol: string; seniorApy: number | null; juniorApy: number | null; decimals: number };
type TrancheBalance = { market_address: string; tranche_side: "senior" | "junior"; lp_balance: string };
type TrancheStats = { netValue: number; pnl: number };
type ClmmMarket = { address: string; vault_address: string; current_implied_apy: number | null };
type ClmmBalance = { position_address: string; market_address: string; lp_balance: string; lower_tick: number; upper_tick: number };
type ClmmObservation = { costBasisValue: number; positionValue: number; farmRewardsUsd: number };

export type Tranche = { market: string; side: "senior" | "junior"; symbol: string; lpBalance: number; valueUsd: number; pnl: number; apy: number | null };
export type Liquidity = {
  vault: string;
  name: string;
  lowerApyPct: number;
  upperApyPct: number;
  inRange: boolean | null;
  valueUsd: number | null;
  pnl: number | null;
  farmRewardsUsd: number | null;
};

export type Extras = { tranches: Tranche[]; liquidity: Liquidity[]; failed: boolean; cachedAgeMs: number | null };

const nonZero = (balance: string) => balance !== "0" && Number(balance) > 0;

export async function getExtras(wallet: string, onre: OnReMarket[]): Promise<Extras> {
  const extras: Extras = { tranches: [], liquidity: [], failed: false, cachedAgeMs: null };
  const noteAge = (ageMs: number | null) => {
    if (ageMs != null) extras.cachedAgeMs = Math.max(extras.cachedAgeMs ?? 0, ageMs);
  };
  const fail = (what: string, e: unknown) => {
    console.error(`Exponent ${what} unavailable:`, String(e));
    extras.failed = true;
  };

  const [trancheMarkets, clmmMarkets, trancheBalances, clmmBalances] = await Promise.allSettled([
    getShared<TrancheMarket[]>("/tranching-markets"),
    getShared<ClmmMarket[]>("/clmm"),
    apiGet<TrancheBalance[]>(`/balances/user/tranching/${wallet}`),
    apiGet<ClmmBalance[]>(`/balances/user/clmm-lp/${wallet}`),
  ]);

  // Protected: senior/junior tranche positions in OnRe tranching markets
  if (trancheBalances.status === "rejected") fail("tranche balances", trancheBalances.reason);
  else if (trancheMarkets.status === "rejected") fail("tranche markets", trancheMarkets.reason);
  else {
    noteAge(trancheMarkets.value.ageMs);
    const markets = new Map(trancheMarkets.value.value.filter((m) => m.platformName === "OnRe" || m.baseTokenSymbol === "ONyc").map((m) => [m.address, m]));
    const held = trancheBalances.value.filter((b) => nonZero(b.lp_balance) && markets.has(b.market_address));
    const results = await Promise.allSettled(
      held.map(async (b) => {
        const market = markets.get(b.market_address)!;
        const scale = 10 ** (market.decimals ?? 9);
        const stats = await apiGet<TrancheStats>(`/tranching-markets/${b.market_address}/user-position/stats?walletAddress=${wallet}&trancheSide=${b.tranche_side}`);
        return {
          market: b.market_address,
          side: b.tranche_side,
          symbol: `${b.tranche_side === "senior" ? "sr" : "jr"}${market.baseTokenSymbol}`,
          lpBalance: Number(b.lp_balance) / scale,
          valueUsd: stats.netValue / scale,
          pnl: stats.pnl / scale,
          apy: b.tranche_side === "senior" ? market.seniorApy : market.juniorApy,
        } satisfies Tranche;
      }),
    );
    for (const r of results) {
      if (r.status === "fulfilled") extras.tranches.push(r.value);
      else fail("tranche stats", r.reason);
    }
  }

  // Liquidity: CLMM LP positions in the OnRe yield markets
  if (clmmBalances.status === "rejected") fail("LP balances", clmmBalances.reason);
  else if (clmmMarkets.status === "rejected") fail("LP markets", clmmMarkets.reason);
  else {
    noteAge(clmmMarkets.value.ageMs);
    const onreByVault = new Map(onre.map((m) => [m.vault, m]));
    const clmmByAddress = new Map(clmmMarkets.value.value.map((m) => [m.address, m]));
    const held = clmmBalances.value.filter((b) => nonZero(b.lp_balance) && onreByVault.has(clmmByAddress.get(b.market_address)?.vault_address ?? ""));
    const results = await Promise.allSettled(
      held.map(async (b) => {
        const clmm = clmmByAddress.get(b.market_address)!;
        const market = onreByVault.get(clmm.vault_address)!;
        // Ticks are APY in 1e-6 units: 60000 -> 6.00%
        const lowerApyPct = b.lower_tick / 10_000;
        const upperApyPct = b.upper_tick / 10_000;
        const apyPct = clmm.current_implied_apy != null ? clmm.current_implied_apy * 100 : null;

        let valueUsd: number | null = null;
        let pnl: number | null = null;
        let farmRewardsUsd: number | null = null;
        const history = await apiGet<ClmmObservation[]>(`/clmm/historical/user/${wallet}?positionAddress=${b.position_address}&lookbackPeriod=1d`);
        const latest = history.at(-1);
        if (latest) {
          const scale = 10 ** market.decimals;
          valueUsd = latest.positionValue / scale;
          farmRewardsUsd = latest.farmRewardsUsd;
          // The API does not define a PnL for LP positions; this estimate is value - cost basis + farm rewards.
          pnl = (latest.positionValue - latest.costBasisValue) / scale + latest.farmRewardsUsd;
        }
        return {
          vault: clmm.vault_address,
          name: market.name,
          lowerApyPct,
          upperApyPct,
          inRange: apyPct == null ? null : apyPct >= lowerApyPct && apyPct <= upperApyPct,
          valueUsd,
          pnl,
          farmRewardsUsd,
        } satisfies Liquidity;
      }),
    );
    for (const r of results) {
      if (r.status === "fulfilled") extras.liquidity.push(r.value);
      else fail("LP value", r.reason);
    }
  }

  return extras;
}
