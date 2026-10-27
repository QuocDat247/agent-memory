using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace PersonalLearningAgent;

internal sealed record ChatTextSegment(string Text, string? Color);

internal sealed record ChatExportData(DateTimeOffset SentAt, IReadOnlyList<ChatTextSegment> Segments);

internal static class ChatExporter
{
    public static void WriteWord(string path, ChatExportData message)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new W.Body();

        body.Append(new W.Paragraph(
            new W.Run(
                new W.RunProperties(new W.Bold()),
                new W.Text($"Thời gian: {message.SentAt.ToLocalTime():dd/MM/yyyy HH:mm}"))));

        var contentParagraph = new W.Paragraph();
        foreach (var segment in message.Segments)
        {
            AppendWordText(contentParagraph, segment);
        }

        body.Append(contentParagraph);
        mainPart.Document = new W.Document(body);
        mainPart.Document.Save();
    }

    public static void WriteExcel(string path, ChatExportData message)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

        var timestampRow = new S.Row(
            CreateTextCell("A1", "Thời gian"),
            CreateTextCell("B1", message.SentAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")));
        var contentCell = new S.Cell
        {
            CellReference = "B2",
            DataType = S.CellValues.InlineString,
            InlineString = CreateInlineString(message.Segments)
        };
        var contentRow = new S.Row(CreateTextCell("A2", "Nội dung"), contentCell);

        worksheetPart.Worksheet = new S.Worksheet(new S.SheetData(timestampRow, contentRow));
        worksheetPart.Worksheet.Save();

        var sheets = workbookPart.Workbook = new S.Workbook(new S.Sheets());
        var sheet = new S.Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Tin nhắn"
        };
        sheets.Sheets!.Append(sheet);
        sheets.Save();
    }

    private static void AppendWordText(W.Paragraph paragraph, ChatTextSegment segment)
    {
        var normalizedText = segment.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalizedText.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Length > 0)
            {
                var run = new W.Run();
                if (TryGetColor(segment.Color, out var color))
                {
                    run.RunProperties = new W.RunProperties(new W.Color { Val = color });
                }

                run.Append(new W.Text(lines[index]) { Space = SpaceProcessingModeValues.Preserve });
                paragraph.Append(run);
            }

            if (index < lines.Length - 1)
            {
                paragraph.Append(new W.Run(new W.Break()));
            }
        }
    }

    private static S.InlineString CreateInlineString(IEnumerable<ChatTextSegment> segments)
    {
        var inlineString = new S.InlineString();
        foreach (var segment in segments)
        {
            if (segment.Text.Length == 0)
            {
                continue;
            }

            var run = new S.Run();
            if (TryGetColor(segment.Color, out var color))
            {
                run.RunProperties = new S.RunProperties(
                    new S.Color { Rgb = $"FF{color}" });
            }

            run.Append(new S.Text(segment.Text) { Space = SpaceProcessingModeValues.Preserve });
            inlineString.Append(run);
        }

        return inlineString;
    }

    private static S.Cell CreateTextCell(string cellReference, string text) =>
        new()
        {
            CellReference = cellReference,
            DataType = S.CellValues.InlineString,
            InlineString = new S.InlineString(new S.Text(text))
        };

    private static bool TryGetColor(string? color, out string hexColor)
    {
        hexColor = string.Empty;
        if (color is not { Length: 7 } || color[0] != '#')
        {
            return false;
        }

        hexColor = color[1..];
        return hexColor.All(Uri.IsHexDigit);
    }
}
