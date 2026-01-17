using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkteTimer.Services;

public sealed class RvgFeeTableService
{
    private static readonly Lazy<RvgFeeTable> TableInstance = new(LoadTable);

    public RvgFeeTable Table => TableInstance.Value;

    public decimal LookupFee1_0(decimal subjectValueEur)
    {
        var entries = Table.Entries;
        if (entries.Count == 0)
        {
            return 0m;
        }

        var value = Math.Max(0m, subjectValueEur);
        foreach (var entry in entries)
        {
            if (entry.ValueToEur >= value)
            {
                return entry.Fee1_0Eur;
            }
        }

        return entries[^1].Fee1_0Eur;
    }

    private static RvgFeeTable LoadTable()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "AkteTimer.Resources.rvg_fee_table.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"RVG Tabelle '{resourceName}' nicht gefunden.");
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var table = JsonSerializer.Deserialize<RvgFeeTable>(json, options);
        if (table == null || table.Entries.Count == 0)
        {
            throw new InvalidOperationException("RVG Tabelle konnte nicht geladen werden.");
        }

        return table;
    }
}

public sealed record RvgFeeTable(
    [property: JsonPropertyName("entries")] IReadOnlyList<RvgFeeTableEntry> Entries,
    [property: JsonPropertyName("metadata")] RvgFeeTableMetadata Metadata);

public sealed record RvgFeeTableEntry(
    [property: JsonPropertyName("value_to_eur")] int ValueToEur,
    [property: JsonPropertyName("fee_1_0_eur")] decimal Fee1_0Eur);

public sealed record RvgFeeTableMetadata(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("version_label")] string VersionLabel,
    [property: JsonPropertyName("version_date")] string VersionDate);
