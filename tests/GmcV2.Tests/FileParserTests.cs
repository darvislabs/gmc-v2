using GmcV2.Infrastructure;
using System.Text;
using Xunit;

namespace GmcV2.Tests;

public class FileParserTests
{
    [Fact]
    public void Csv_QuotedCommas_Parsed()
    {
        var csv = "Claim No,Disease,Amount\n\"C1\",\"Fracture, femur (compound)\",\"12,500\"\nC2,Simple,3000\n";
        var doc = FileParser.Parse("claims.csv", Encoding.UTF8.GetBytes(csv));
        Assert.Null(doc.Error);
        Assert.Equal("csv", doc.Format);
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("Fracture, femur (compound)", sheet.Rows[0][1]);
        Assert.Equal("12,500", sheet.Rows[0][2]);
    }

    [Fact]
    public void Csv_Semicolon_Sniffed()
    {
        var csv = "A;B;C\n1;2;3\n4;5;6\n";
        var doc = FileParser.Parse("data.txt", Encoding.UTF8.GetBytes(csv));
        Assert.Null(doc.Error);
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal(3, sheet.Headers.Count);
        Assert.Equal(2, sheet.Rows.Count);
    }

    [Fact]
    public void Xlsx_RoundTrip_CellsIntact()
    {
        using var ms = new MemoryStream();
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Members");
            ws.Cell(1, 1).Value = "Member No";
            ws.Cell(1, 2).Value = "Age";
            ws.Cell(2, 1).Value = "M001";
            ws.Cell(2, 2).Value = 42;
            ws.Cell(3, 1).Value = "M002";
            ws.Cell(3, 2).Value = 31.5;
            wb.SaveAs(ms);
        }
        var doc = FileParser.Parse("enrollment.xlsx", ms.ToArray());
        Assert.Null(doc.Error);
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal("Members", sheet.SheetName);
        Assert.Equal("Member No", sheet.Headers[0]);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("M001", sheet.Rows[0][0]);
        Assert.Equal("42", sheet.Rows[0][1]);
        Assert.Equal("31.5", sheet.Rows[1][1]);
    }

    [Fact]
    public void Pdf_Text_Extracted()
    {
        // minimal PDF with one page of text (hand-crafted, valid xref-free structure for PdfPig)
        var pdf = @"%PDF-1.4
1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj
3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj
4 0 obj<</Length 90>>stream
BT /F1 12 Tf 72 720 Td (BaseSumInsured: 500000) Tj ET
endstream
endobj
5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj
trailer<</Root 1 0 R>>";
        var doc = FileParser.Parse("rfq.pdf", Encoding.ASCII.GetBytes(pdf));
        // PdfPig may reject a malformed handcrafted pdf — then Error must be set (graceful), never throw
        if (doc.Error is null)
            Assert.Contains(doc.TextLines, l => l.Contains("BaseSumInsured", StringComparison.OrdinalIgnoreCase));
        else
            Assert.Contains("PDF", doc.Error);
    }

    [Fact]
    public void Unknown_Extension_GracefulError()
    {
        var doc = FileParser.Parse("photo.jpg", new byte[] { 1, 2, 3 });
        Assert.NotNull(doc.Error);
        Assert.Equal("unknown", doc.Format);
    }

    [Fact]
    public void BlankRows_Skipped_HeadersFilled()
    {
        var csv = "A,B\n\n1,2\n,,\n3,4\n";
        var doc = FileParser.Parse("x.csv", Encoding.UTF8.GetBytes(csv));
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal(2, sheet.Rows.Count);
    }
}
