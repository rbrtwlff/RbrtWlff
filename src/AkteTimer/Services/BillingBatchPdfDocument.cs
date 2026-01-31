using System.Globalization;
using AkteTimer.Models;
using AkteTimer.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AkteTimer.Services;

public sealed class BillingBatchPdfDocument : IDocument
{
    private readonly BillingBatch _batch;
    private readonly IReadOnlyList<BillingCasePdfData> _cases;
    private readonly CultureInfo _culture;

    public BillingBatchPdfDocument(BillingBatch batch, IReadOnlyList<BillingCasePdfData> cases)
    {
        _batch = batch;
        _cases = cases;
        _culture = CultureInfo.GetCultureInfo("de-DE");
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        if (_cases.Count == 0)
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Content()
                    .AlignCenter()
                    .Text("Keine Abrechnungsfälle im Batch.")
                    .FontSize(16)
                    .SemiBold();
            });
            return;
        }

        foreach (var caseData in _cases)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(11));
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Text(text =>
                    {
                        text.Span("Aktenzeichen: ").SemiBold();
                        text.Span(caseData.Matter.FileRef);
                    });
                    column.Item().Text(text =>
                    {
                        text.Span("Batch: ").SemiBold();
                        text.Span($"{_batch.Id} · {_batch.CreatedUtc.ToLocalTime():dd.MM.yyyy}");
                    });

                    column.Item().Element(container => BuildTimeEntriesTable(container, caseData));

                    if (caseData.BillingCase.BillingType == BillingType.Hourly)
                    {
                        column.Item().Element(container => BuildHourlySummary(container, caseData));
                    }
                    else
                    {
                        column.Item().Element(container => BuildRvgSection(container, caseData));
                    }

                    column.Item().Element(container => BuildStaffNote(container, caseData));
                });

                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Seite ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }
    }

    private void BuildTimeEntriesTable(IContainer container, BillingCasePdfData caseData)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("Zeiteinträge").FontSize(13).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(70);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).Text("Datum");
                    header.Cell().Element(HeaderCellStyle).Text("Dauer");
                    header.Cell().Element(HeaderCellStyle).Text("Notiz");
                });

                if (caseData.TimeEntries.Count == 0)
                {
                    table.Cell().ColumnSpan(3).Element(CellStyle).Text("Keine Zeiteinträge.");
                }
                else
                {
                    foreach (var entry in caseData.TimeEntries)
                    {
                        var duration = TimeEntryCalculations.GetDuration(entry);
                        table.Cell().Element(CellStyle).Text(entry.StartUtc.ToLocalTime().ToString("dd.MM.yyyy", _culture));
                        table.Cell().Element(CellStyle).Text(duration.ToString(@"hh\:mm"));
                        table.Cell().Element(CellStyle).Text(entry.Note ?? string.Empty);
                    }
                }
            });
        });
    }

    private void BuildHourlySummary(IContainer container, BillingCasePdfData caseData)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("Stundenhonorar").FontSize(13).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(90);
                    columns.ConstantColumn(90);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).Text("Position");
                    header.Cell().Element(HeaderCellStyle).Text("Zeit");
                    header.Cell().Element(HeaderCellStyle).Text("Betrag");
                });

                AddSummaryRow(table, "Erfasst", caseData.BillingCase.TrackedMinutes, caseData.BillingCase.TrackedAmount, false);
                AddSummaryRow(table, "Dummy/Nachtrag", caseData.BillingCase.DummyMinutes, caseData.BillingCase.DummyAmount, true);
                AddSummaryRow(table, "Summe", caseData.BillingCase.TotalMinutes, caseData.BillingCase.TotalAmount, false, isTotal: true);
            });
        });
    }

    private void BuildRvgSection(IContainer container, BillingCasePdfData caseData)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("RVG").FontSize(13).SemiBold();
            column.Item().Text(text =>
            {
                text.Span("Tatbestand: ").SemiBold();
                text.Span(caseData.RvgSignature);
            });

            column.Item().Text(text =>
            {
                text.Span("Streitwert: ").SemiBold();
                text.Span(FormatCurrency(caseData.Matter.SubjectValueEur));
            });

            column.Item().Text(text =>
            {
                text.Span("Gebühren: ").SemiBold();
                text.Span(caseData.RvgFeeSummary);
            });

            column.Item().Text(text =>
            {
                text.Span("Betrag: ").SemiBold();
                text.Span(FormatCurrency(caseData.BillingCase.RvgTotal));
            });

            if (caseData.BillingCase.RvgIsDifference)
            {
                column.Item().Text("Differenzabrechnung").FontColor(Colors.Red.Darken1).SemiBold();
                column.Item().Text(text =>
                {
                    text.Span("Neuer Gesamtbetrag: ").SemiBold();
                    text.Span(FormatCurrency(caseData.BillingCase.RvgBaseTotal + caseData.BillingCase.RvgTotal));
                });
                column.Item().Text(text =>
                {
                    text.Span("Basis-Snapshot: ").SemiBold();
                    text.Span($"{FormatCurrency(caseData.BillingCase.RvgBaseTotal)}");
                });
                if (!string.IsNullOrWhiteSpace(caseData.BillingCase.RvgBaseSignature))
                {
                    column.Item().Text(text =>
                    {
                        text.Span("Basis-Signatur: ").SemiBold();
                        text.Span(caseData.BillingCase.RvgBaseSignature);
                    });
                }
            }
        });
    }

    private void BuildStaffNote(IContainer container, BillingCasePdfData caseData)
    {
        var noteText = string.IsNullOrWhiteSpace(caseData.BillingCase.NoteForStaff)
            ? " "
            : caseData.BillingCase.NoteForStaff;

        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text("Notiz fürs Sekretariat").FontSize(13).SemiBold();
            column.Item().Border(1).Padding(8).MinHeight(60).Text(noteText);
        });
    }

    private void AddSummaryRow(TableDescriptor table, string label, int minutes, decimal amount, bool isDummy, bool isTotal = false)
    {
        table.Cell().Element(container =>
        {
            container = CellStyle(container);
            if (isDummy)
            {
                container = container.Background(Colors.Yellow.Lighten4);
            }
            if (isTotal)
            {
                container = container.Background(Colors.Grey.Lighten4);
            }

            return container;
        }).Text(label);
        table.Cell().Element(CellStyle).Text(FormatMinutes(minutes));
        table.Cell().Element(CellStyle).Text(FormatCurrency(amount));
    }

    private string FormatCurrency(decimal amount) => $"{amount:N2} €";

    private string FormatMinutes(int minutes) => TimeSpan.FromMinutes(minutes).ToString(@"hh\:mm");

    private static IContainer HeaderCellStyle(IContainer container)
    {
        return container
            .DefaultTextStyle(style => style.SemiBold())
            .PaddingVertical(4)
            .PaddingHorizontal(6)
            .Background(Colors.Grey.Lighten3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Medium);
    }

    private static IContainer CellStyle(IContainer container)
    {
        return container
            .PaddingVertical(4)
            .PaddingHorizontal(6)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2);
    }
}

public sealed record BillingCasePdfData(
    BillingCase BillingCase,
    Matter Matter,
    IReadOnlyList<TimeEntry> TimeEntries,
    string RvgSignature,
    string RvgFeeSummary);
