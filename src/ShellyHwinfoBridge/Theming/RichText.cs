using ShellyHwinfoBridge.Core.Presentation;

namespace ShellyHwinfoBridge.Theming;

/// <summary>
/// Lays out and draws <see cref="InlineMarkup"/> text (<c>**bold**</c>) with GDI (TextRenderer), word-wrapped to a
/// width: regular runs in one font, bold runs in another. Words are measured one at a time, so a line breaks only
/// between words (a single word wider than the line is placed on its own line).
/// </summary>
internal static class RichText
{
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    /// <summary>Returns whether <paramref name="text"/> contains markup worth the rich layout.</summary>
    public static bool HasMarkup(string? text) => text is not null && text.Contains("**", StringComparison.Ordinal);

    /// <summary>Measures the wrapped text for <paramref name="width"/> pixels.</summary>
    public static Size Measure(string? markup, Font regular, Font bold, int width)
    {
        var pieces = Layout(markup, regular, bold, width);
        var lineHeight = LineHeight(regular, bold);
        if (pieces.Count == 0)
        {
            return new Size(0, lineHeight);
        }

        return new Size(pieces.Max(p => p.Bounds.Right), pieces.Max(p => p.Bounds.Bottom));
    }

    /// <summary>Draws the wrapped text inside <paramref name="bounds"/>.</summary>
    public static void Draw(Graphics g, string? markup, Font regular, Font bold, Rectangle bounds, Color color)
    {
        foreach (var piece in Layout(markup, regular, bold, bounds.Width))
        {
            var r = piece.Bounds;
            r.Offset(bounds.Location);
            if (r.Top >= bounds.Bottom)
            {
                break;
            }

            TextRenderer.DrawText(g, piece.Text, piece.Font, r.Location, color, Flags);
        }
    }

    private static int LineHeight(Font regular, Font bold) =>
        Math.Max(TextRenderer.MeasureText("Ag", regular, Size.Empty, Flags).Height, TextRenderer.MeasureText("Ag", bold, Size.Empty, Flags).Height);

    private static List<(string Text, Font Font, Rectangle Bounds)> Layout(string? markup, Font regular, Font bold, int width)
    {
        var result = new List<(string, Font, Rectangle)>();
        var lineHeight = LineHeight(regular, bold);
        var x = 0;
        var y = 0;
        var afterSpace = true;
        foreach (var run in InlineMarkup.Parse(markup))
        {
            var font = run.Bold ? bold : regular;
            var lines = run.Text.Split('\n');
            for (var l = 0; l < lines.Length; l++)
            {
                if (l > 0)
                {
                    x = 0;
                    y += lineHeight;
                    afterSpace = true;
                }

                foreach (var token in Tokens(lines[l]))
                {
                    var size = TextRenderer.MeasureText(token, font, Size.Empty, Flags);
                    var isSpace = string.IsNullOrWhiteSpace(token);
                    // Break only between words: "**OK**." keeps the full stop with the bold word.
                    if (!isSpace && afterSpace && x > 0 && x + size.Width > width)
                    {
                        x = 0;
                        y += lineHeight;
                    }

                    if (isSpace && x == 0)
                    {
                        // No leading spaces on a wrapped line.
                        continue;
                    }

                    if (!isSpace)
                    {
                        result.Add((token, font, new Rectangle(x, y, size.Width, lineHeight)));
                    }

                    x += size.Width;
                    afterSpace = isSpace;
                }
            }
        }

        return result;
    }

    /// <summary>Splits text into alternating word and space tokens ("a b" → "a", " ", "b").</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || char.IsWhiteSpace(text[i]) != char.IsWhiteSpace(text[start]))
            {
                yield return text[start..i];
                start = i;
            }
        }
    }
}
