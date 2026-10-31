namespace OnReBot.Models;

public class LoopscaleVaultDeposits
{
    public string VaultAddress { get; set; } = "";
    public List<LoopscaleUserDeposit> UserDeposits { get; set; } = new();
}

public class LoopscaleUserDeposit
{
    public string UserAddress { get; set; } = "";
    public long AmountSupplied { get; set; }
}

public class LoopscalePosition
{
    public string VaultName { get; set; } = "";

    public decimal Usdc { get; set; }

    public int? CachedSeconds { get; set; }
}
