using System.Text.RegularExpressions;

namespace OnReBot.Services;

public static class AddressValidator
{
    // base58 (بدون 0، O، I، l) با طول ۳۲ تا ۴۴
    private static readonly Regex Base58 = new("^[1-9A-HJ-NP-Za-km-z]{32,44}$", RegexOptions.Compiled);

    public static bool IsValid(string? address) =>
        address != null && Base58.IsMatch(address);
}
