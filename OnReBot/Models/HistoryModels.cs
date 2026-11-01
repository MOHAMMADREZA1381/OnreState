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
