using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CybeneticsImport;

/// <summary>Turns a PDF into text lines with PdfPig, rebuilding lines from word positions.</summary>
public static class PdfTextExtractor
{
    /// <summary>Extracts the lines of every page, top to bottom, words left to right separated by one space.</summary>
    /// <param name="path">The PDF file.</param>
    public static IReadOnlyList<string> ExtractLines(string path)
    {
        using var document = PdfDocument.Open(path);
        var lines = new List<string>();
        foreach (var page in document.GetPages())
        {
            lines.AddRange(GroupIntoLines(page.GetWords()));
        }

        return lines;
    }

    /// <summary>Groups words whose baselines are within half a word height into lines.</summary>
    /// <param name="words">The words of one page.</param>
    internal static IEnumerable<string> GroupIntoLines(IEnumerable<Word> words)
    {
        var rows = new List<(double Baseline, double Height, List<Word> Words)>();
        foreach (var word in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var baseline = word.BoundingBox.Bottom;
            var height = Math.Max(word.BoundingBox.Height, 1);
            var index = rows.FindIndex(r => Math.Abs(r.Baseline - baseline) <= Math.Max(r.Height, height) * 0.5);
            if (index < 0)
            {
                rows.Add((baseline, height, [word]));
            }
            else
            {
                rows[index].Words.Add(word);
            }
        }

        return rows
            .OrderByDescending(r => r.Baseline)
            .Select(r => string.Join(' ', r.Words.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text.Trim())));
    }
}
