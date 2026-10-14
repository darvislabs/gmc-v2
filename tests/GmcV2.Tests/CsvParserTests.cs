using GmcV2.Core;
using System.Text;
using Xunit;

public class CsvParserTests
{
    [Fact]
    public void ParseCsv_PlainAscii_Header_FirstChar_Intact()
    {
        var bytes = Encoding.ASCII.GetBytes("BenefitCode,UtilizationRate\nMaternityNormal,0.12\n");
        var doc = GmcV2.Infrastructure.FileParser.Parse("m.csv", bytes);
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal("BenefitCode", sheet.Headers[0]);          // was "enefitCode" — the BOM-strip bug ate char 1
        Assert.Equal("UtilizationRate", sheet.Headers[1]);
        Assert.Equal(1, sheet.Rows.Count);
        Assert.Equal("MaternityNormal", sheet.Rows[0][0]);
    }

    [Fact]
    public void ParseCsv_WithBom_Header_FirstChar_Intact()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("Claim No,Member No\nC1,M1\n")).ToArray();
        var doc = GmcV2.Infrastructure.FileParser.Parse("m.csv", bytes);
        var sheet = Assert.Single(doc.Sheets);
        Assert.Equal("Claim No", sheet.Headers[0]);             // BOM stripped, 'C' intact
    }
}
