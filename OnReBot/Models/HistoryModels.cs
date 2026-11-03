namespace OnReBot.Models;

// Kamino: daily snapshots of one obligation, summarized as first and last value.
public class KaminoHistory
{
    public string MarketName { get; set; } = "";
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public decimal FirstNetValueUsd { get; set; }
    public decimal LastNetValueUsd { get; set; }
    public int Points { get; set; }
}

public class KaminoHistoryResponse
{
    public List<KaminoHistoryPoint> History { get; set; } = new();
}

public class KaminoHistoryPoint
{
    public DateTimeOffset Timestamp { get; set; }
    public KaminoStats RefreshedStats { get; set; } = new();
}

// Exponent: one YT trade (amounts already scaled to token units; price is SY per YT).
public class ExponentTrade
{
    public DateTimeOffset At { get; set; }
    public string Market { get; set; } = "";
    public bool IsBuy { get; set; }
    public decimal Yt { get; set; }
    public decimal Sy { get; set; }
    public decimal Price { get; set; }

    // true: the market has matured (it is no longer an active OnRe market)
    public bool Matured { get; set; }

    // Value of the trade in USD (the API reports SY amounts, and 1 SY is worth $1, so this is the SY amount)
    public decimal ValueUsd { get; set; }
}

public class ExponentHistory
{
    public List<ExponentTrade> Trades { get; set; } = new();

    // How many YT trades the wallet has in OnRe markets in total (Trades only holds the most recent ones)
    public int TotalTrades { get; set; }

    // true: the Exponent API could not be read
    public bool Failed { get; set; }
}

public class HistoryReport
{
    public List<KaminoHistory> Kamino { get; set; } = new();
    public bool KaminoFailed { get; set; }
    public ExponentHistory Exponent { get; set; } = new();
}

// Exponent: current state of the active OnRe markets (the same for every wallet).
public class ExponentOverview
{
    public List<MarketInfo> Markets { get; set; } = new();
    public List<TrancheInfo> Tranches { get; set; } = new();

    // null: everything was fresh; a number: part of the data came from the Node service's cache.
    public int? CachedAgeSeconds { get; set; }
}

public class MarketInfo
{
    public string Name { get; set; } = "";
    public decimal? ImpliedApy { get; set; }
    public decimal? UnderlyingApy { get; set; }
    public decimal? YtPrice { get; set; }
    public decimal? PtPrice { get; set; }

    // USD value of one token of the market (the SY exchange rate)
    public decimal? TokenPrice { get; set; }
    public DateTimeOffset Maturity { get; set; }
}

// One swap quote ONyc <-> USDC (from Jupiter).
public record SwapRate
{
    // true: USDC -> ONyc (buying ONyc); false: ONyc -> USDC (selling ONyc)
    public bool Buy { get; init; }

    // The DEX this price is for; null for an aggregator's own best route (which can mix several DEXes)
    public string? Dex { get; init; }

    // The aggregator that gave a best-route price ("Jupiter", "Titan"); null for a single-DEX price
    public string? Source { get; init; }

    public decimal AmountIn { get; init; }
    public decimal AmountOut { get; init; }
    public decimal UsdPerOnyc { get; init; }
    public decimal PriceImpactPct { get; init; }
    public List<string> Routes { get; init; } = new();

    // null: fetched just now; a number: served from the short shared cache
    public int? CachedSeconds { get; init; }
}

public class SwapRates
{
    public List<SwapRate> Rates { get; set; } = new();
    public bool Failed { get; set; }
}

public class TrancheInfo
{
    public string Symbol { get; set; } = "";
    public decimal? SeniorApy { get; set; }
    public decimal? JuniorApy { get; set; }
}
