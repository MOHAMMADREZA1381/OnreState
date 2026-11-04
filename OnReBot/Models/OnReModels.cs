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

    // The points per platform, non-zero ones only. They add up to the wallet's total points (checked against the real leaderboard:
    // 49 of the top 50 wallets match to the point; the OnRe API itself is off for one of them).
    public List<(string Name, decimal Points)> Platforms()
    {
        var list = new List<(string, decimal)>();

        void Add(string name, decimal points)
        {
            if (points > 0)
                list.Add((name, points));
        }

        Add("Wallet", Wallet);
        Add("Permissionless Boost", PermissionlessBoost);
        Add("Kamino", Kamino.Values.Sum());
        Add("Exponent", Exponent.Total);
        Add("Loopscale", Loopscale.Values.Sum());
        Add("Ratex", Ratex);
        Add("Carrot", Carrot);
        Add("Carrot Lending", CarrotLending);
        Add("Elemental", Elemental);
        Add("Exponent Vault", ExponentVault);
        Add("OnRe Yield+", OnreYieldPlus);
        Add("Orca", Orca.Values.Sum());
        Add("Referral bonus", ReferralBonus);
        return list;
    }
}

public class ExponentPoints
{
    public decimal Yt { get; set; }
    public decimal Lp { get; set; }

    public Dictionary<string, ExponentMarketPoints> Markets { get; set; } = new();

    // Points of the Protected (senior / junior tranche) positions
    public ExponentTranchingPoints Tranching { get; set; } = new();

    // Everything OnRe counts as Exponent points. The per-market numbers in Markets are a split of Yt and Lp, so they are not added again.
    public decimal Total => Yt + Lp + Tranching.Senior + Tranching.Junior;
}

public class ExponentTranchingPoints
{
    public decimal Senior { get; set; }
    public decimal Junior { get; set; }
}

public class ExponentMarketPoints
{
    public decimal Yt { get; set; }
    public decimal Lp { get; set; }
}
