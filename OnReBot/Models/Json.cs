using System.Text.Json;

namespace OnReBot.Models;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}
