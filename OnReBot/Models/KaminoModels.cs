namespace OnReBot.Models;

// فقط فیلدهای لازم از پاسخ حجیم Kamino
public class KaminoObligation
{
    public string ObligationAddress { get; set; } = "";
    public KaminoState State { get; set; } = new();
    public KaminoStats RefreshedStats { get; set; } = new();
}

public class KaminoState
{
    public List<KaminoDeposit> Deposits { get; set; } = new();
    public int HasDebt { get; set; }
}

public class KaminoDeposit
{
    public string DepositReserve { get; set; } = "";
    public string DepositedAmount { get; set; } = "0";
}

public class KaminoStats
{
    public string UserTotalDeposit { get; set; } = "0";
    public string UserTotalBorrow { get; set; } = "0";
    public string NetAccountValue { get; set; } = "0";
    public string LoanToValue { get; set; } = "0";
    public string BorrowLimit { get; set; } = "0";
}

public class KaminoTokenAmount
{
    public string Symbol { get; set; } = "";
    public decimal Amount { get; set; }
}

public class KaminoPosition
{
    public string MarketName { get; set; } = "";
    public List<KaminoTokenAmount> Tokens { get; set; } = new();
    public decimal TotalDepositUsd { get; set; }
    public decimal TotalBorrowUsd { get; set; }
    public decimal NetValueUsd { get; set; }
    public decimal LoanToValue { get; set; }
}
