using System.Globalization;

namespace CybeneticsImport;

/// <summary>The command-line front end: single import, plus the <c>batch</c>, <c>merge</c> and <c>download</c> verbs.</summary>
public static class ImportCommand
{
    /// <summary>Exit code for success.</summary>
    public const int Ok = 0;

    /// <summary>Exit code when parsing or validation failed.</summary>
    public const int Failed = 1;

    /// <summary>Exit code for invalid arguments.</summary>
    public const int Usage = 2;

    private const string Help = """
        Usage:
          CybeneticsImport [import] <report.pdf|page.html|report.txt> [more inputs...] [options]   import one entry
          CybeneticsImport import --html <saved evaluation page.html> [options]
          CybeneticsImport batch --catalog <csv> --reports <dir> --out <dir>    import every catalogue row
          CybeneticsImport merge --entries <dir> --into <psu-database.json>     merge entries into the database
          CybeneticsImport download --catalog <csv> --reports <dir>             fetch missing report PDFs (see README)

        Single import: builds one psu-database.json entry from Cybenetics report(s).

        Inputs (.pdf is read with PdfPig; .html/.htm, or any file starting with <html or <!doctype html, is read as a
        saved Cybenetics evaluation page; anything else is read as extracted text, one line per row):
          <path>                       voltage taken from the page's voltage tabs or markers in the text
          --html <path>                a saved evaluation page; brand, model, revision, wattage, report id and URL are
                                       taken from it unless given (and id derived from brand, model and revision)
          --input230 <path>            input whose rows are all 230 V
          --input115 <path>            input whose rows are all 115 V

        Metadata (required unless noted):
          --id <id>  --brand <text>  --series <text>  --model <text>  [--revision <text>]
          --wattage <W>  --rating80plus <White|Bronze|Silver|Gold|Platinum|Titanium|None>
          [--ratingCybenetics <text>]  --reportId <id>  --url <report page URL>

        Output:
          [--out <file>]               write the entry to a file instead of stdout
          [--dump-text]                print the extracted lines (numbered) to stderr, to tune the locators

        Parser tuning (regular expressions, case-insensitive; see README; also accepted by batch):
          [--voltage <115|230>]        voltage for rows before any marker in auto inputs
          [--light-load-header <re>] [--load-header <re>] [--section-end <re>] [--voltage-marker <re>]
          [--light-load-row <re>] [--load-row <re>] [--ignore-line <re>] [--dc-tolerance <fraction>]
        """;

    private static readonly string[] TuningNames =
        ["voltage", "light-load-header", "load-header", "section-end", "voltage-marker", "light-load-row", "load-row", "ignore-line", "dc-tolerance"];

    private static readonly string[] MetadataNames =
        ["id", "brand", "series", "model", "revision", "wattage", "rating80plus", "ratingcybenetics", "reportid", "url"];

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Standard output (the entry JSON).</param>
    /// <param name="stderr">Standard error (warnings, summary).</param>
    /// <param name="readLines">Reads an input's lines (injectable for tests); PDF/text by extension by default.</param>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IReadOnlyList<string>>? readLines = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        readLines ??= EntryImporter.ReadInput;

        if (args.Length == 0 || args.Any(a => a is "-h" or "--help" or "/?"))
        {
            stdout.WriteLine(Help);
            return args.Length == 0 ? Usage : Ok;
        }

        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "batch":
                    return BatchCommand.Run(OptionSet.Parse(args[1..], ["catalog", "reports", "out", .. TuningNames], []), stderr, readLines);
                case "merge":
                    return MergeCommand.Run(OptionSet.Parse(args[1..], ["entries", "into", "date"], []), stderr, DateTime.Today);
                case "download":
                    return DownloadCommand.Run(OptionSet.Parse(args[1..], ["catalog", "reports"], []), stderr);
                case "import":
                    return RunImport(args[1..], stdout, stderr, readLines);
                default:
                    return RunImport(args, stdout, stderr, readLines);
            }
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            stderr.WriteLine("Run with --help for usage.");
            return Usage;
        }
    }

    /// <summary>Creates parser options from tuning values (<c>--voltage</c>, locators, <c>--dc-tolerance</c>).</summary>
    /// <param name="values">The parsed options.</param>
    /// <exception cref="ArgumentException">A value is invalid (including a regular expression that does not compile).</exception>
    internal static ParserOptions CreateParserOptions(OptionSet values)
    {
        var options = new ParserOptions();
        if (values.Get("voltage") is { } v)
        {
            options.DefaultVoltage = v.Trim() switch
            {
                "115" => 115,
                "230" => 230,
                _ => throw new ArgumentException("--voltage must be 115 or 230."),
            };
        }

        options.LightLoadHeader = values.Get("light-load-header") ?? options.LightLoadHeader;
        options.LoadTableHeader = values.Get("load-header") ?? options.LoadTableHeader;
        options.SectionEnd = values.Get("section-end") ?? options.SectionEnd;
        options.VoltageMarker = values.Get("voltage-marker") ?? options.VoltageMarker;
        options.LightLoadRow = values.Get("light-load-row") ?? options.LightLoadRow;
        options.LoadRow = values.Get("load-row") ?? options.LoadRow;
        options.IgnoreLine = values.Get("ignore-line") ?? options.IgnoreLine;
        if (values.Get("dc-tolerance") is { } tol)
        {
            options.DcTolerance = double.TryParse(tol, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0 && t < 1
                ? t
                : throw new ArgumentException("--dc-tolerance must be a fraction between 0 and 1.");
        }

        _ = new ReportParser(options); // Compiles the regexes; a bad pattern throws ArgumentException.
        return options;
    }

    private static int RunImport(string[] args, TextWriter stdout, TextWriter stderr, Func<string, IReadOnlyList<string>> readLines)
    {
        var values = OptionSet.Parse(args, ["input230", "input115", "html", "out", .. MetadataNames, .. TuningNames], ["dump-text"]);
        var inputs = values.Positional.Concat(values.All("html")).Select(p => new ImportInput(p, null))
            .Concat(values.All("input230").Select(p => new ImportInput(p, 230)))
            .Concat(values.All("input115").Select(p => new ImportInput(p, 115)))
            .ToList();
        if (inputs.Count == 0)
        {
            throw new ArgumentException("No input file given.");
        }

        foreach (var html in values.All("html").Where(p => !HtmlReportParser.IsHtml(p)))
        {
            throw new ArgumentException($"--html {html}: not an .html/.htm file or HTML page.");
        }

        var metadata = EntryMetadata.FromValues(EntryImporter.WithPageMetadata(MetadataNames.ToDictionary(n => n, values.Get), inputs));
        _ = CreateParserOptions(values);

        var result = EntryImporter.Import(metadata, inputs, () => CreateParserOptions(values), readLines, stderr, values.Flag("dump-text"));
        if (result.Entry is null)
        {
            stderr.WriteLine("error: " + result.Error);
            return Failed;
        }

        var json = EntryWriter.Format(result.Entry);
        if (values.Get("out") is { } outPath)
        {
            File.WriteAllText(outPath, json);
            stderr.WriteLine($"Wrote {outPath}");
        }
        else
        {
            stdout.Write(json);
        }

        return Ok;
    }
}

/// <summary>Parsed <c>--name value</c> options, flags and positional arguments.</summary>
internal sealed class OptionSet
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the arguments that are not options.</summary>
    public List<string> Positional { get; } = [];

    /// <summary>Parses arguments.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="valueNames">Options that take a value (without <c>--</c>).</param>
    /// <param name="flagNames">Options without a value.</param>
    /// <exception cref="ArgumentException">An unknown option or a missing value.</exception>
    public static OptionSet Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> valueNames, IReadOnlyCollection<string> flagNames)
    {
        var result = new OptionSet();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                result.Positional.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (flagNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result._flags.Add(name);
                continue;
            }

            if (!valueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown option {arg}.");
            }

            if (i + 1 >= args.Count)
            {
                throw new ArgumentException($"{arg} needs a value.");
            }

            if (!result._values.TryGetValue(name, out var list))
            {
                result._values[name] = list = [];
            }

            list.Add(args[++i]);
        }

        return result;
    }

    /// <summary>Returns the last value of an option, or <c>null</c>.</summary>
    /// <param name="name">The option name without <c>--</c>.</param>
    public string? Get(string name) => _values.TryGetValue(name, out var list) ? list[^1] : null;

    /// <summary>Returns all values of an option.</summary>
    /// <param name="name">The option name without <c>--</c>.</param>
    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var list) ? list : [];

    /// <summary>Returns whether a flag was given.</summary>
    /// <param name="name">The flag name without <c>--</c>.</param>
    public bool Flag(string name) => _flags.Contains(name);

    /// <summary>Returns a required option's value.</summary>
    /// <param name="name">The option name without <c>--</c>.</param>
    /// <exception cref="ArgumentException">The option is missing.</exception>
    public string Require(string name) =>
        string.IsNullOrWhiteSpace(Get(name)) ? throw new ArgumentException($"--{name} is required.") : Get(name)!;
}
