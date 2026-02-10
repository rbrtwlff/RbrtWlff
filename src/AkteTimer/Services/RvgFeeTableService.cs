using System.Collections.Generic;
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

        var feeAt500k = LookupFee(entries, 500000m);
        var stepsAbove500k = Math.Ceiling((value - 500000m) / 50000m);
        return feeAt500k + (stepsAbove500k * 175m);
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

        ValidateTable(table);
        return table;
    }

    private static void ValidateTable(RvgFeeTable table)
    {
        var entries = table.Entries;
        var lastValue = -1;
        var lastFee = decimal.MinValue;
        foreach (var entry in entries)
        {
            if (entry.ValueToEur <= lastValue)
            {
                throw new InvalidOperationException($"RVG-Tabelle inkonsistent/ungültig: Wertstufen nicht aufsteigend bei value_to_eur={entry.ValueToEur}.");
            }

            if (entry.Fee1_0Eur < lastFee)
            {
                throw new InvalidOperationException($"RVG-Tabelle inkonsistent/ungültig: 1,0-Gebühr fällt ab bei value_to_eur={entry.ValueToEur}.");
            }

            lastValue = entry.ValueToEur;
            lastFee = entry.Fee1_0Eur;
        }

        var fixpoints = new Dictionary<decimal, decimal>
        {
            { 500m, 51.50m },
            { 1000m, 93.00m },
            { 3000m, 235.50m },
            { 10000m, 652.00m },
            { 50000m, 1357.00m }
        };

        foreach (var fixpoint in fixpoints)
        {
            var fee = LookupFee(entries, fixpoint.Key);
            if (fee != fixpoint.Value)
            {
                throw new InvalidOperationException(
                    $"RVG-Tabelle inkonsistent/ungültig: Fixpunkt {fixpoint.Key:N0} EUR erwartet {fixpoint.Value:N2} EUR, gefunden {fee:N2} EUR.");
            }
        }
    }

    private static decimal LookupFee(IReadOnlyList<RvgFeeTableEntry> entries, decimal subjectValueEur)
    {
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
