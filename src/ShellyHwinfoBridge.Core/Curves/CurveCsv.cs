using System.Globalization;
using System.Text;

namespace ShellyHwinfoBridge.Core.Curves;

/// <summary>A problem found while parsing a curve CSV file.</summary>
/// <param name="LineNumber">1-based line number.</param>
/// <param name="Message">Description of the problem.</param>
public sealed record CurveCsvError(int LineNumber, string Message);

/// <summary>The result of parsing a curve CSV file.</summary>
/// <param name="Points">The parsed rows (unvalidated as a curve).</param>
/// <param name="Errors">Parse errors; empty on success.</param>
public sealed record CurveCsvParseResult(IReadOnlyList<CurvePoint> Points, IReadOnlyList<CurveCsvError> Errors)
{
    /// <summary>Gets whether parsing succeeded without errors.</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Imports and exports curves as CSV with header <c>ac_w,dc_w</c>, using the invariant culture
/// (dot decimal separator, comma field separator) regardless of the system locale.
/// </summary>
public static class CurveCsv
{
    /// <summary>The CSV header line.</summary>
    public const string Header = "ac_w,dc_w";

    /// <summary>Exports points to CSV text.</summary>
    /// <param name="points">The curve rows.</param>
    public static string Export(IEnumerable<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var sb = new StringBuilder();
        sb.Append(Header).Append("\r\n");
        foreach (var p in points)
        {
            sb.Append(p.AcWatts.ToString(CultureInfo.InvariantCulture))
              .Append(',')
              .Append(p.DcWatts.ToString(CultureInfo.InvariantCulture))
              .Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses CSV text. The header line is optional; blank lines are ignored. Curve rules are not checked here;
    /// use <see cref="CurveValidator"/> on the result.
    /// </summary>
    /// <param name="text">The CSV text.</param>
    public static CurveCsvParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var points = new List<CurvePoint>();
        var errors = new List<CurveCsvError>();
        var lines = text.TrimStart('﻿').Split('\n');
        var seenContent = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var lineNumber = i + 1;
            if (!seenContent)
            {
                seenContent = true;
                if (string.Equals(line.Replace(" ", string.Empty, StringComparison.Ordinal), Header, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var fields = line.Split(',');
            if (fields.Length != 2)
            {
                errors.Add(new CurveCsvError(lineNumber, $"Line {lineNumber}: expected 2 fields but found {fields.Length}."));
                continue;
            }

            if (!TryParseNumber(fields[0], out var ac) || !TryParseNumber(fields[1], out var dc))
            {
                errors.Add(new CurveCsvError(lineNumber, $"Line {lineNumber}: values must be numbers with a dot decimal separator."));
                continue;
            }

            points.Add(new CurvePoint(ac, dc));
        }

        return new CurveCsvParseResult(points, errors);
    }

    private static bool TryParseNumber(string field, out double value) =>
        double.TryParse(field.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
