using ShellyHwinfoBridge.Core.Database;

namespace CybeneticsImport;

/// <summary>One input document and the voltage all of its rows belong to (<c>null</c>: taken from the text).</summary>
/// <param name="Path">The PDF or extracted-text file.</param>
/// <param name="Voltage">115, 230 or <c>null</c>.</param>
/// <param name="DefaultVoltage">For a <c>null</c> <paramref name="Voltage"/>: the voltage of rows before any marker.</param>
public sealed record ImportInput(string Path, int? Voltage, int? DefaultVoltage = null);

/// <summary>The outcome of importing one entry.</summary>
/// <param name="Entry">The validated entry, or <c>null</c> on failure.</param>
/// <param name="Error">Why the import failed, or <c>null</c>.</param>
/// <param name="Warnings">Warnings worth a manual look (skipped rows, missing labels, efficiency mismatches).</param>
/// <param name="PointCounts">Points per voltage.</param>
public sealed record ImportResult(
    PsuEntry? Entry,
    string? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<int, int> PointCounts);

/// <summary>Turns report inputs plus metadata into one validated database entry.</summary>
public static class EntryImporter
{
    /// <summary>The 15 row labels a complete curve has.</summary>
    public static IReadOnlyList<string> ExpectedLabels { get; } =
        [.. new[] { "20W", "40W", "60W", "80W" }.Concat(Enumerable.Range(1, 11).Select(i => $"{i * 10}%"))];

    /// <summary>Imports one entry.</summary>
    /// <param name="metadata">The entry metadata.</param>
    /// <param name="inputs">The input documents.</param>
    /// <param name="createOptions">Creates fresh parser options (locators) for each input.</param>
    /// <param name="readLines">Reads an input's text lines.</param>
    /// <param name="log">Receives the per-voltage summary, warnings and (optionally) the extracted text.</param>
    /// <param name="dumpText">Whether to write the numbered extracted lines to <paramref name="log"/>.</param>
    public static ImportResult Import(
        EntryMetadata metadata,
        IReadOnlyList<ImportInput> inputs,
        Func<ParserOptions> createOptions,
        Func<string, IReadOnlyList<string>> readLines,
        TextWriter log,
        bool dumpText = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(createOptions);
        ArgumentNullException.ThrowIfNull(readLines);
        ArgumentNullException.ThrowIfNull(log);

        var warnings = new List<string>();
        void Warn(string message)
        {
            warnings.Add(message);
            log.WriteLine("warning: " + message);
        }

        var points = new List<MeasuredPoint>();
        foreach (var input in inputs)
        {
            if (HtmlReportParser.IsHtml(input.Path))
            {
                ParseResult page;
                try
                {
                    page = HtmlReportParser.Parse(File.ReadAllText(input.Path), metadata.Wattage, input.Voltage).Result;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Fail($"cannot read {input.Path}: {ex.Message}");
                }

                Collect(Path.GetFileName(input.Path), page);
                continue;
            }

            IReadOnlyList<string> lines;
            try
            {
                lines = readLines(input.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                or FormatException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
            {
                return Fail($"cannot read {input.Path}: {ex.Message}");
            }

            if (dumpText)
            {
                log.WriteLine($"--- {input.Path} ({lines.Count} lines) ---");
                for (var i = 0; i < lines.Count; i++)
                {
                    log.WriteLine($"{i + 1,5}: {lines[i]}");
                }
            }

            var options = createOptions();
            options.ForcedVoltage = input.Voltage;
            options.DefaultVoltage = input.DefaultVoltage ?? options.DefaultVoltage;
            Collect(Path.GetFileName(input.Path), new ReportParser(options).Parse(lines, metadata.Wattage));
        }

        void Collect(string file, ParseResult result)
        {
            foreach (var warning in result.Warnings)
            {
                Warn($"{file}: {warning}");
            }

            foreach (var point in result.Points)
            {
                if (points.Any(p => p.Voltage == point.Voltage && p.Label == point.Label))
                {
                    Warn($"{file}: {point.Voltage}V {point.Label} already read from an earlier input; ignored.");
                    continue;
                }

                points.Add(point);
            }
        }

        var curves = new ParseResult(points, []).Curves;
        foreach (var (volts, curve) in curves)
        {
            var labels = points.Where(p => p.Voltage == volts).Select(p => p.Label).ToList();
            log.WriteLine($"{volts} V: {curve.Count} points ({string.Join(", ", labels)})");
            var missing = ExpectedLabels.Except(labels).ToList();
            if (missing.Count > 0)
            {
                Warn($"{volts} V is missing {string.Join(", ", missing)}; check the report manually.");
            }
        }

        if (curves.Count == 1)
        {
            Warn($"only {curves.Keys.Single()} V was found; check whether the report (or a separate document) has the other voltage.");
        }

        var counts = curves.ToDictionary(c => c.Key, c => c.Value.Count);
        if (curves.Count == 0)
        {
            return Fail("no curve points found. Use --dump-text to inspect the extracted text and adjust the locators.");
        }

        var entry = EntryWriter.Build(metadata, curves);
        var errors = EntryWriter.Validate(entry);
        if (errors.Count > 0)
        {
            return Fail("the entry fails database validation: " + string.Join("; ", errors));
        }

        return new ImportResult(entry, null, warnings, counts);

        ImportResult Fail(string error) =>
            new(null, error, warnings, points.GroupBy(p => p.Voltage).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>
    /// Completes metadata values with what the first saved evaluation page (HTML input) says; given values win.
    /// </summary>
    /// <param name="values">The values from the command line or catalogue (keys as in <see cref="EntryMetadata.FromValues"/>).</param>
    /// <param name="inputs">The inputs.</param>
    public static Dictionary<string, string?> WithPageMetadata(IReadOnlyDictionary<string, string?> values, IEnumerable<ImportInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(inputs);
        var result = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        var page = inputs.FirstOrDefault(i => HtmlReportParser.IsHtml(i.Path));
        if (page is null)
        {
            return result;
        }

        string html;
        try
        {
            html = File.ReadAllText(page.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result; // The import itself reports the unreadable file.
        }

        foreach (var (key, value) in HtmlReportParser.Parse(html).Metadata.ToValues())
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault(key)))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Reads an input: <c>.pdf</c> with PdfPig, anything else as extracted text, one line per row.</summary>
    /// <param name="path">The file.</param>
    public static IReadOnlyList<string> ReadInput(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? PdfTextExtractor.ExtractLines(path)
            : File.ReadAllLines(path);
}
