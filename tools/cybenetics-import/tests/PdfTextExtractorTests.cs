using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CybeneticsImport.Tests;

public sealed class PdfTextExtractorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "cyb-import-" + Guid.NewGuid().ToString("N") + ".pdf");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void RebuildsLinesFromWordPositionsAcrossPages()
    {
        var fixture = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tx700-stacked.txt"))
            .Where(l => l.Length > 0)
            .Select(l => l.Replace("°", string.Empty, StringComparison.Ordinal)) // Not in the standard Helvetica encoding.
            .ToList();
        WritePdf(fixture, linesPerPage: 40);

        var lines = PdfTextExtractor.ExtractLines(_path);

        Assert.Equal(fixture, lines);
        var result = new ReportParser().Parse(lines, 700);
        Assert.Empty(result.Warnings);
        Assert.Equal(15, result.Curves[230].Count);
        Assert.Equal(15, result.Curves[115].Count);
    }

    private void WritePdf(IReadOnlyList<string> lines, int linesPerPage)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var start = 0; start < lines.Count; start += linesPerPage)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 800.0;
            foreach (var line in lines.Skip(start).Take(linesPerPage))
            {
                // Place each word separately, like a table with columns.
                var x = 30.0;
                foreach (var word in line.Split(' '))
                {
                    page.AddText(word, 8, new PdfPoint(x, y), font);
                    x += (word.Length * 5.0) + 8;
                }

                y -= 14;
            }
        }

        File.WriteAllBytes(_path, builder.Build());
    }
}
