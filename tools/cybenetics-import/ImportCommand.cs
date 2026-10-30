using System.Globalization;
using ShellyHwinfoBridge.Core.Curves;

namespace CybeneticsImport;

/// <summary>The command-line front end.</summary>
public static class ImportCommand
{
    /// <summary>Exit code for success.</summary>
    public const int Ok = 0;

    /// <summary>Exit code when parsing or validation failed.</summary>
    public const int Failed = 1;

    /// <summary>Exit code for invalid arguments.</summary>
    public const int Usage = 2;

    private const string Help = """
        Usage: CybeneticsImport <report.pdf|report.txt> [more inputs...] [options]

        Builds one psu-database.json entry from Cybenetics report(s) downloaded manually.

        Inputs (.pdf is read with PdfPig, any other extension is read as extracted text, one line per row):
          <path>                       voltage taken from markers in the text ("230V", "115V")
          --input230 <path>            input whose rows are all 230 V
          --input115 <path>            input whose rows are all 115 V

        Metadata (required unless noted):
          --id <id>  --brand <text>  --series <text>  --model <text>  [--revision <text>]
          --wattage <W>  --rating80plus <White|Bronze|Silver|Gold|Platinum|Titanium|None>
          [--ratingCybenetics <text>]  --reportId <id>  --url <report URL>

        Output:
          [--out <file>]               write the entry to a file instead of stdout
          [--dump-text]                print the extracted lines (numbered) to stderr, to tune the locators

        Parser tuning (regular expressions, case-insensitive; see README):
          [--voltage <115|230>]        voltage for rows before any marker in auto inputs
          [--light-load-header <re>] [--load-header <re>] [--section-end <re>] [--voltage-marker <re>]
          [--light-load-row <re>] [--load-row <re>] [--dc-tolerance <fraction>]
        """;

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Standard output (the entry JSON).</param>
    /// <param name="stderr">Standard error (warnings, summary).</param>
    /// <param name="readLines">Reads an input's lines (injectable for tests); PDF/text by extension by default.</param>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IReadOnlyList<string>>? readLines = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        readLines ??= ReadInput;

        if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "/?"))
        {
            stdout.WriteLine(Help);
            return args.Length == 0 ? Usage : Ok;
        }

        Arguments parsed;
        try
        {
            parsed = Arguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            stderr.WriteLine("Run with --help for usage.");
            return Usage;
        }

        var points = new List<MeasuredPoint>();
        foreach (var (path, voltage) in parsed.Inputs)
        {
            IReadOnlyList<string> lines;
            try
            {
                lines = readLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                stderr.WriteLine($"error: cannot read {path}: {ex.Message}");
                return Failed;
            }

            if (parsed.DumpText)
            {
                stderr.WriteLine($"--- {path} ({lines.Count} lines) ---");
                for (var i = 0; i < lines.Count; i++)
                {
                    stderr.WriteLine($"{i + 1,5}: {lines[i]}");
                }
            }

            var options = parsed.CreateOptions();
            options.ForcedVoltage = voltage;
            var result = new ReportParser(options).Parse(lines, parsed.Metadata.Wattage);
            foreach (var warning in result.Warnings)
            {
                stderr.WriteLine($"warning: {Path.GetFileName(path)}: {warning}");
            }

            foreach (var point in result.Points)
            {
                if (points.Any(p => p.Voltage == point.Voltage && p.Label == point.Label))
                {
                    stderr.WriteLine($"warning: {Path.GetFileName(path)}: {point.Voltage}V {point.Label} already read from an earlier input; ignored.");
                    continue;
                }

                points.Add(point);
            }
        }

        var curves = new ParseResult(points, []).Curves;
        foreach (var (volts, curve) in curves)
        {
            var labels = points.Where(p => p.Voltage == volts).Select(p => p.Label).ToList();
            stderr.WriteLine($"{volts} V: {curve.Count} points ({string.Join(", ", labels)})");
            var missing = ExpectedLabels.Except(labels).ToList();
            if (missing.Count > 0)
            {
                stderr.WriteLine($"warning: {volts} V is missing {string.Join(", ", missing)}; check the report manually.");
            }
        }

        if (curves.Count == 0)
        {
            stderr.WriteLine("error: no curve points found. Use --dump-text to inspect the extracted text and adjust the locators.");
            return Failed;
        }

        var entry = EntryWriter.Build(parsed.Metadata, curves);
        var errors = EntryWriter.Validate(entry);
        foreach (var error in errors)
        {
            stderr.WriteLine("error: " + error);
        }

        if (errors.Count > 0)
        {
            return Failed;
        }

        var json = EntryWriter.Format(entry);
        if (parsed.OutPath is null)
        {
            stdout.Write(json);
        }
        else
        {
            File.WriteAllText(parsed.OutPath, json);
            stderr.WriteLine($"Wrote {parsed.OutPath}");
        }

        return Ok;
    }

    private static IEnumerable<string> ExpectedLabels =>
        new[] { "20W", "40W", "60W", "80W" }.Concat(Enumerable.Range(1, 11).Select(i => $"{i * 10}%"));

    private static IReadOnlyList<string> ReadInput(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? PdfTextExtractor.ExtractLines(path)
            : File.ReadAllLines(path);

    private sealed class Arguments
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Path, int? Voltage)> Inputs { get; } = [];

        public EntryMetadata Metadata { get; } = new();

        public string? OutPath => _values.GetValueOrDefault("out");

        public bool DumpText { get; private set; }

        public static Arguments Parse(string[] args)
        {
            var result = new Arguments();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    result.Inputs.Add((arg, null));
                    continue;
                }

                var name = arg[2..];
                if (name.Equals("dump-text", StringComparison.OrdinalIgnoreCase))
                {
                    result.DumpText = true;
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"{arg} needs a value.");
                }

                var value = args[++i];
                switch (name.ToLowerInvariant())
                {
                    case "input230":
                        result.Inputs.Add((value, 230));
                        break;
                    case "input115":
                        result.Inputs.Add((value, 115));
                        break;
                    case "id" or "brand" or "series" or "model" or "revision" or "wattage" or "rating80plus" or "ratingcybenetics"
                        or "reportid" or "url" or "out" or "voltage" or "light-load-header" or "load-header" or "section-end"
                        or "voltage-marker" or "light-load-row" or "load-row" or "dc-tolerance":
                        result._values[name] = value;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option {arg}.");
                }
            }

            result.FillMetadata();
            return result;
        }

        public ParserOptions CreateOptions()
        {
            var options = new ParserOptions();
            if (_values.TryGetValue("voltage", out var v))
            {
                options.DefaultVoltage = ParseVoltage(v);
            }

            options.LightLoadHeader = _values.GetValueOrDefault("light-load-header") ?? options.LightLoadHeader;
            options.LoadTableHeader = _values.GetValueOrDefault("load-header") ?? options.LoadTableHeader;
            options.SectionEnd = _values.GetValueOrDefault("section-end") ?? options.SectionEnd;
            options.VoltageMarker = _values.GetValueOrDefault("voltage-marker") ?? options.VoltageMarker;
            options.LightLoadRow = _values.GetValueOrDefault("light-load-row") ?? options.LightLoadRow;
            options.LoadRow = _values.GetValueOrDefault("load-row") ?? options.LoadRow;
            if (_values.TryGetValue("dc-tolerance", out var tol))
            {
                options.DcTolerance = double.TryParse(tol, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0 && t < 1
                    ? t
                    : throw new ArgumentException("--dc-tolerance must be a fraction between 0 and 1.");
            }

            return options;
        }

        private static int ParseVoltage(string value) => value.Trim() switch
        {
            "115" => 115,
            "230" => 230,
            _ => throw new ArgumentException("--voltage must be 115 or 230."),
        };

        private void FillMetadata()
        {
            if (Inputs.Count == 0)
            {
                throw new ArgumentException("No input file given.");
            }

            foreach (var required in new[] { "id", "brand", "series", "model", "wattage", "rating80plus", "reportid", "url" })
            {
                if (string.IsNullOrWhiteSpace(_values.GetValueOrDefault(required)))
                {
                    throw new ArgumentException($"--{required} is required.");
                }
            }

            if (!int.TryParse(_values["wattage"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var watts) || watts <= 0)
            {
                throw new ArgumentException("--wattage must be a positive whole number.");
            }

            var rating = _values["rating80plus"].Trim();
            var canonical = Enum.GetNames<EightyPlusRating>().Append("None").FirstOrDefault(n => n.Equals(rating, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("--rating80plus must be White, Bronze, Silver, Gold, Platinum, Titanium or None.");

            Metadata.Id = _values["id"].Trim();
            Metadata.Brand = _values["brand"].Trim();
            Metadata.Series = _values["series"].Trim();
            Metadata.Model = _values["model"].Trim();
            Metadata.Revision = _values.GetValueOrDefault("revision")?.Trim();
            Metadata.Wattage = watts;
            Metadata.Rating80Plus = canonical;
            Metadata.RatingCybenetics = _values.GetValueOrDefault("ratingcybenetics")?.Trim();
            Metadata.ReportId = _values["reportid"].Trim();
            Metadata.Url = _values["url"].Trim();
            _ = new ReportParser(CreateOptions()); // Validate the tuning options and regexes early.
        }
    }
}
