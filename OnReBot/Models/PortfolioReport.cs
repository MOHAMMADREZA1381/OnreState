namespace OnReBot.Models;

public class ExponentPosition
{
    // Farm (YT) | Protected (tranche) | Liquidity (CLMM LP)
    public string Section { get; set; } = "Farm";
    public string Market { get; set; } = "";
    public string Details { get; set; } = "";

    public int? CachedSeconds { get; set; }
}

public class ExponentResult
{
    public List<ExponentPosition> Positions { get; set; } = new();

    // true: Protected/Liquidity positions could not be loaded (the Exponent API was unavailable)
    public bool ExtrasFailed { get; set; }
}

public class PortfolioReport
{
    public OnReResponse? OnRe { get; set; }
    public bool OnReFailed { get; set; }

    public List<KaminoPosition> Kamino { get; set; } = new();
    public bool KaminoFailed { get; set; }

    public List<LoopscalePosition> Loopscale { get; set; } = new();
    public bool LoopscaleFailed { get; set; }

    public List<ExponentPosition> Exponent { get; set; } = new();
    public bool ExponentFailed { get; set; }
    public bool ExponentExtrasFailed { get; set; }
}
