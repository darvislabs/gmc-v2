using System.Text;
using GmcV2.Core;
using UglyToad.PdfPig;

namespace GmcV2.Infrastructure;

/// <summary>
/// Real file parsing: xlsx (ClosedXML), csv (RFC4180 + delimiter sniffing), pdf text (PdfPig).
/// Structure only — never interprets content. Every cell is a trimmed string.
/// </summary>
public static class FileParser
{
    public static ParsedDocument Parse(string fileName, byte[] content)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ParseXlsx(content),
            ".csv" => ParseCsv(content, ','),
            ".txt" or ".tsv" => SniffAndParse(content),
            ".pdf" => ParsePdf(content),
            _ => new ParsedDocument { FileName = fileName ?? "", Format = "unknown", Error = $"Unsupported file type '{ext}' — upload .xlsx, .csv, .txt or .pdf." },
        };
    }

    private static ParsedDocument ParseXlsx(byte[] content)
    {
        try
        {
            using var ms = new MemoryStream(content);
            using var wb = new ClosedXML.Excel.XLWorkbook(ms);
            var doc = new ParsedDocument { FileName = "", Format = "xlsx" };
            foreach (var ws in wb.Worksheets)
            {
                var sheet = new ParsedSheet { SheetName = ws.Name };
                var used = ws.RangeUsed();
                if (used is null) { doc.Sheets.Add(sheet); continue; }
                foreach (var row in used.Rows())
                {
                    var cells = new List<string>();
                    foreach (var cell in row.Cells())
                        cells.Add(cell.DataType switch
                        {
                            ClosedXML.Excel.XLDataType.Number => cell.GetDouble().ToString("0.############", System.Globalization.CultureInfo.InvariantCulture),
                            ClosedXML.Excel.XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd"),
                            ClosedXML.Excel.XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
                            _ => cell.GetString().Trim(),
                        });
                    if (cells.All(string.IsNullOrWhiteSpace)) continue; // skip blank rows
                    sheet.Rows.Add(cells.ToArray());
                }
                if (sheet.Rows.Count > 0)
                {
                    sheet.Headers = sheet.Rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column{i + 1}" : h).ToList();
                    sheet.Rows.RemoveAt(0);
                }
                doc.Sheets.Add(sheet);
            }
            return doc;
        }
        catch (Exception ex)
        {
            return new ParsedDocument { FileName = "", Format = "xlsx", Error = $"Workbook could not be read: {Truncate(ex.Message)}" };
        }
    }

    private static ParsedDocument SniffAndParse(byte[] content)
    {
        // text file: use csv parsing if commas/semicolons/tab found in first lines, else text lines
        var text = Encoding.UTF8.GetString(content);
        var firstLines = text.Split('\n').Take(3);
        if (firstLines.Any(l => l.Contains(','))) return ParseCsv(content, ',');
        if (firstLines.Any(l => l.Contains(';'))) return ParseCsv(content, ';');
        if (firstLines.Any(l => l.Contains('\t'))) return ParseCsv(content, '\t');
        var doc = new ParsedDocument { FileName = "", Format = "txt", TextLines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList() };
        return doc;
    }

    private static ParsedDocument ParseCsv(byte[] content, char delimiter)
    {
        try
        {
            var text = Encoding.UTF8.GetString(content);
            // Robust BOM strip: check the first CODEPOINT. (A StartsWith("\uFEFF") check here
            // mis-fired on plain-ASCII input and consumed the first character of every CSV.)
            if (text.Length > 0 && text[0] == '﻿') text = text[1..];
            var doc = new ParsedDocument { FileName = "", Format = "csv" };
            var sheet = new ParsedSheet { SheetName = "Sheet1" };
            foreach (var row in ParseCsvLines(text, delimiter))
            {
                if (row.All(string.IsNullOrWhiteSpace)) continue;
                sheet.Rows.Add(row);
            }
            if (sheet.Rows.Count > 0)
            {
                sheet.Headers = sheet.Rows[0].Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"Column{i + 1}" : h).ToList();
                sheet.Rows.RemoveAt(0);
            }
            doc.Sheets.Add(sheet);
            return doc;
        }
        catch (Exception ex)
        {
            return new ParsedDocument { FileName = "", Format = "csv", Error = $"CSV could not be read: {Truncate(ex.Message)}" };
        }
    }

    /// <summary>RFC4180: quoted fields, escaped quotes, embedded newlines.</summary>
    internal static List<string[]> ParseCsvLines(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) inQuotes = true;
            else if (ch == delimiter) { row.Add(field.ToString().Trim()); field.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString().Trim()); field.Clear();
                if (row.Any(c => c.Length > 0)) rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().Trim());
            if (row.Any(c => c.Length > 0)) rows.Add(row.ToArray());
        }
        return rows;
    }

    private static ParsedDocument ParsePdf(byte[] content)
    {
        try
        {
            var doc = new ParsedDocument { FileName = "", Format = "pdf", TextLines = new List<string>() };
            using var pdf = PdfDocument.Open(content);
            foreach (var page in pdf.GetPages())
            {
                var text = page.Text ?? "";
                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) doc.TextLines.Add(t);
                }
            }
            return doc;
        }
        catch (Exception ex)
        {
            return new ParsedDocument { FileName = "", Format = "pdf", Error = $"PDF could not be read: {Truncate(ex.Message)}" };
        }
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];
}
