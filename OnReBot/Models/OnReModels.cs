namespace OnReBot.Models;

public class OnReResponse
{
    public int Rank { get; set; }
    public string Address { get; set; } = "";
    public decimal TotalPoints { get; set; }
    public OnRePointsBreakdown PointsBreakdown { get; set; } = new();
}

public class OnRePointsBreakdown
{
    public decimal Wallet { get; set; }
    public decimal PermissionlessBoost { get; set; }
    public Dictionary<string, decimal> Kamino { get; set; } = new();
    public ExponentPoints Exponent { get; set; } = new();
    public Dictionary<string, decimal> Loopscale { get; set; } = new();
    public decimal Ratex { get; set; }
    public decimal Carrot { get; set; }
    public decimal CarrotLending { get; set; }
    public decimal Elemental { get; set; }
    public decimal ExponentVault { get; set; }
    public decimal OnreYieldPlus { get; set; }
    public Dictionary<string, decimal> Orca { get; set; } = new();
    public decimal ReferralBonus { get; set; }
}

public class ExponentPoints
{
    public decimal Yt { get; set; }
    public decimal Lp { get; set; }

    public Dictionary<string, ExponentMarketPoints> Markets { get; set; } = new();
}

public class ExponentMarketPoints
{
    public decimal Yt { get; set; }
    public decimal Lp { get; set; }
}
