using System.Text;

namespace ShellyHwinfoBridge.Core.Presentation;

/// <summary>A piece of text with one style.</summary>
/// <param name="Text">The text.</param>
/// <param name="Bold">Whether it is drawn bold.</param>
public sealed record TextRun(string Text, bool Bold);

/// <summary>
/// Minimal inline markup for UI strings: <c>**text**</c> is bold (names of tabs, buttons and options). An unmatched
/// <c>**</c> is kept as text; there is no escaping and no nesting.
/// </summary>
public static class InlineMarkup
{
    private const string Marker = "**";

    /// <summary>Splits <paramref name="markup"/> into runs (empty runs are dropped).</summary>
    /// <param name="markup">The text with markup.</param>
    public static IReadOnlyList<TextRun> Parse(string? markup)
    {
        var runs = new List<TextRun>();
        if (string.IsNullOrEmpty(markup))
        {
            return runs;
        }

        var position = 0;
        while (position < markup.Length)
        {
            var open = markup.IndexOf(Marker, position, StringComparison.Ordinal);
            var close = open < 0 ? -1 : markup.IndexOf(Marker, open + Marker.Length, StringComparison.Ordinal);
            if (open < 0 || close < 0)
            {
                Add(runs, markup[position..], false);
                break;
            }

            Add(runs, markup[position..open], false);
            Add(runs, markup[(open + Marker.Length)..close], true);
            position = close + Marker.Length;
        }

        return runs;
    }

    /// <summary>Returns the text without markup (for accessibility and logs).</summary>
    /// <param name="markup">The text with markup.</param>
    public static string Strip(string? markup)
    {
        var text = new StringBuilder();
        foreach (var run in Parse(markup))
        {
            text.Append(run.Text);
        }

        return text.ToString();
    }

    private static void Add(List<TextRun> runs, string text, bool bold)
    {
        if (text.Length > 0)
        {
            runs.Add(new TextRun(text, bold));
        }
    }
}
