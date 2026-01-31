using System.Text.Json;
using AkteTimer.Models;

namespace AkteTimer.Services;

public static class RvgBreakdownSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(RvgBreakdown breakdown)
    {
        return JsonSerializer.Serialize(breakdown, Options);
    }

    public static RvgBreakdown? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<RvgBreakdown>(json, Options);
    }
}
