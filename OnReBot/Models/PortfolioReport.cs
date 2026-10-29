namespace OnReBot.Models;

public class ExponentPosition
{
    public string Market { get; set; } = "";
    public string Details { get; set; } = "";

    // null: داده تازه است. عدد: بخشی از داده از کش آمده و حداکثر این‌قدر ثانیه قدمت دارد.
    public int? CachedSeconds { get; set; }
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
}
