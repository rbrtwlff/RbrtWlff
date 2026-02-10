using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

var repoRoot = FindRepoRoot();
var outputPath = Path.Combine(repoRoot, "src", "AkteTimer", "Resources", "rvg_fee_table.json");

const string primarySource = "https://www.gesetze-im-internet.de/rvg/anlage_2.html";
const string fallbackSource = "https://raw.githubusercontent.com/QuantLaw/gesetze-im-internet/data/data/items/rvg/BJNR078800004.xml";

var sourceData = await DownloadSource(primarySource, fallbackSource);
var entries = ParseEntriesFromXml(sourceData.content);

if (entries.Count == 0)
{
    throw new InvalidOperationException("Keine RVG-Einträge aus der Quelle extrahiert.");
}

var payload = new
{
    metadata = new
    {
        source = sourceData.source,
        version_label = "Anlage 2 zu § 13 RVG",
        version_date = "generated"
    },
    entries = entries.Select(e => new { value_to_eur = e.ValueToEur, fee_1_0_eur = e.Fee1_0Eur })
};

var options = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(payload, options);
await File.WriteAllTextAsync(outputPath, json + Environment.NewLine, new UTF8Encoding(false));

Console.WriteLine($"RVG-Tabelle aktualisiert: {outputPath}");
Console.WriteLine($"Quelle: {sourceData.source}");
Console.WriteLine($"Einträge: {entries.Count}");

static async Task<(string source, string content)> DownloadSource(string primary, string fallback)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AkteTimer-RVG-Generator/1.0");

    try
    {
        _ = await client.GetStringAsync(primary);
        throw new InvalidOperationException("Primärquelle liefert HTML; XML-Fallback wird für robustes Parsing verwendet.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Primärquelle nicht erreichbar/verarbeitbar ({primary}): {ex.Message}");
        Console.Error.WriteLine($"Verwende Fallback-Quelle: {fallback}");
        var content = await client.GetStringAsync(fallback);
        return (fallback, content);
    }
}

static List<RvgEntry> ParseEntriesFromXml(string xmlContent)
{
    var doc = XDocument.Parse(xmlContent);
    var norms = doc.Root?.Elements("norm") ?? Enumerable.Empty<XElement>();

    XElement? targetTable = null;
    foreach (var norm in norms)
    {
        var enbez = norm.Element("metadaten")?.Element("enbez")?.Value ?? string.Empty;
        if (!enbez.Contains("Anlage 2", StringComparison.Ordinal))
        {
            continue;
        }

        targetTable = norm
            .Descendants("table")
            .FirstOrDefault(table => table.Element("tgroup")?.Attribute("cols")?.Value == "5");

        if (targetTable != null)
        {
            break;
        }
    }

    if (targetTable == null)
    {
        throw new InvalidOperationException("Anlage-2-Tabelle konnte in XML-Quelle nicht gefunden werden.");
    }

    var rows = targetTable
        .Element("tgroup")?
        .Element("tbody")?
        .Elements("row") ?? Enumerable.Empty<XElement>();

    var entries = new List<RvgEntry>();
    foreach (var row in rows)
    {
        var cells = row.Elements("entry").Select(e => string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value))).ToList();
        if (cells.Count < 5)
        {
            continue;
        }

        TryAddEntry(entries, cells[0], cells[1]);
        TryAddEntry(entries, cells[3], cells[4]);
    }

    return entries.OrderBy(e => e.ValueToEur).ToList();
}

static void TryAddEntry(List<RvgEntry> entries, string valueToken, string feeToken)
{
    if (!valueToken.Any(char.IsDigit) || !feeToken.Any(char.IsDigit))
    {
        return;
    }

    var value = ParseInteger(valueToken);
    var fee = ParseDecimal(feeToken);
    entries.Add(new RvgEntry(value, fee));
}

static int ParseInteger(string value)
{
    var normalized = NormalizeToken(value);
    if (normalized.Contains('.'))
    {
        normalized = normalized[..normalized.IndexOf('.')];
    }

    return int.Parse(normalized, CultureInfo.InvariantCulture);
}

static decimal ParseDecimal(string value)
{
    var normalized = NormalizeToken(value);
    return decimal.Parse(normalized, CultureInfo.InvariantCulture);
}

static string NormalizeToken(string token)
{
    return token
        .Replace("\u00A0", " ", StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal)
        .Replace(',', '.')
        .Trim();
}

static string FindRepoRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "AkteTimer.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Repo-Root mit AkteTimer.sln wurde nicht gefunden.");
}

sealed record RvgEntry(int ValueToEur, decimal Fee1_0Eur);
