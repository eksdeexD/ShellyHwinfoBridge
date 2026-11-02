using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace CybeneticsImport;

/// <summary>Metadata found on a saved Cybenetics evaluation page; every value is optional.</summary>
public sealed class PageMetadata
{
    /// <summary>Gets or sets the brand (DUT "Brand" row).</summary>
    public string? Brand { get; set; }

    /// <summary>Gets or sets the model (DUT "Model" row).</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets the platform revision taken from a model suffix such as "(ATX v3.0)".</summary>
    public string? Revision { get; set; }

    /// <summary>
    /// Gets or sets the testing date as printed ("Jul 16, 2021"): the value of the "Testing Date" row of the
    /// "Cybenetics Testing Information" table. See <see cref="ReportDate"/> for the other dates a page carries.
    /// </summary>
    public string? TestingDate { get; set; }

    /// <summary>
    /// Gets the report date written to <c>source.reportDate</c>: <see cref="TestingDate"/> as yyyy-MM-dd, or <c>null</c>
    /// when the page has none or it cannot be read.
    /// </summary>
    /// <remarks>
    /// A page carries several dates; 21PS1872A, for example, has "Certificate Issue Date: Jul 20, 2021" under the report
    /// id at the top, and in the "Cybenetics Testing Information" table "Date of receipt | Jul 7, 2021" (the sample
    /// arrived), "Testing Date | Jul 16, 2021" (the measurements were taken) and "Report Issued Date | Jul 20, 2021".
    /// Only the row labelled exactly "Testing Date" is used, so the entry carries the date the tested sample was measured.
    /// </remarks>
    public string? ReportDate => HtmlReportParser.TryParseReportDate(TestingDate, out var iso) ? iso : null;

    /// <summary>Gets or sets the rated wattage ("Rated Power (W)" row).</summary>
    public int? Wattage { get; set; }

    /// <summary>Gets or sets the report id ("Report ID: 21PS1872A").</summary>
    public string? ReportId { get; set; }

    /// <summary>Gets or sets the report page URL (canonical link or og:url).</summary>
    public string? Url { get; set; }

    /// <summary>Gets or sets the 80 Plus rating, when the page names one.</summary>
    public string? Rating80Plus { get; set; }

    /// <summary>Gets or sets the Cybenetics ETA rating, when the page names one.</summary>
    public string? RatingCybenetics { get; set; }

    /// <summary>Returns the values as metadata keys (see <see cref="EntryMetadata.FromValues"/>), skipping unknown ones.</summary>
    public IReadOnlyDictionary<string, string> ToValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        Add("brand", Brand);
        Add("model", Model);
        Add("revision", Revision);
        Add("wattage", Wattage?.ToString(CultureInfo.InvariantCulture));
        Add("reportid", ReportId);
        Add("url", Url);
        Add("rating80plus", Rating80Plus);
        Add("ratingcybenetics", RatingCybenetics);
        Add("reportdate", ReportDate);
        if (Brand is not null && Model is not null)
        {
            Add("id", Slug((Model.StartsWith(Brand, StringComparison.OrdinalIgnoreCase) ? Model : Brand + " " + Model) + " " + Revision));
        }

        return values;
    }

    /// <summary>Turns text into a lowercase id ("Seasonic Prime Fanless TX-700" → "seasonic-prime-fanless-tx-700").</summary>
    /// <param name="text">The text.</param>
    public static string Slug(string text) =>
        Regex.Replace(text.ToLowerInvariant().Replace("!", string.Empty, StringComparison.Ordinal), "[^a-z0-9]+", "-").Trim('-');
}

/// <summary>The result of parsing a saved evaluation page.</summary>
/// <param name="Result">The points and warnings.</param>
/// <param name="Metadata">What the page says about the unit.</param>
public sealed record HtmlReport(ParseResult Result, PageMetadata Metadata);

/// <summary>
/// Reads a saved Cybenetics evaluation page (the report web page, which carries the test tables as HTML).
/// </summary>
/// <remarks>
/// Every table row is examined, wherever it is: a row is a measurement when its first cell is a load label
/// (<c>10%</c>…<c>110%</c> or <c>20W</c>/<c>40W</c>/<c>60W</c>/<c>80W</c>) and one cell holds a DC/AC pair
/// ("70.006 / 78.495"). Cross-load rows (<c>CL1</c>…) and ripple rows ("10% Load") therefore never match. The voltage
/// comes from the row's AC voltage cell (≥ 180 V → 230 V, else 115 V), or, without one, from the nearest preceding
/// "115V"/"230V" text or a <c>/115/</c> or <c>/230/</c> image path. The printed efficiency is checked against DC/AC.
/// </remarks>
public static partial class HtmlReportParser
{
    /// <summary>Returns whether a file is an HTML page: by extension, or for other non-PDF files by its first bytes.</summary>
    /// <param name="path">The file.</param>
    public static bool IsHtml(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return false;
        }

        using var reader = new StreamReader(path);
        var buffer = new char[2048];
        var head = new string(buffer, 0, reader.Read(buffer, 0, buffer.Length)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a date as Cybenetics prints it ("Jul 16, 2021", "Sep 3, 2024", also "July 16, 2021") or an ISO date
    /// ("2021-07-16") to yyyy-MM-dd.
    /// </summary>
    /// <param name="printed">The date text; surrounding whitespace is ignored.</param>
    /// <param name="iso">The date as yyyy-MM-dd when it could be read; otherwise <c>null</c>.</param>
    public static bool TryParseReportDate(string? printed, [NotNullWhen(true)] out string? iso)
    {
        iso = null;
        if (string.IsNullOrWhiteSpace(printed))
        {
            return false;
        }

        string[] formats = ["MMM d, yyyy", "MMMM d, yyyy", "MMM d yyyy", "MMMM d yyyy", "yyyy-MM-dd"];
        var text = WhitespaceRegex().Replace(printed, " ").Trim();
        if (!DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Reads only the metadata (brand, model, dates, report id, URL, ratings) of a saved evaluation page.</summary>
    /// <param name="html">The page source.</param>
    public static PageMetadata ReadMetadata(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return ReadMetadata(document);
    }

    /// <summary>Parses a saved evaluation page.</summary>
    /// <param name="html">The page source.</param>
    /// <param name="ratedWattage">The rated wattage for the plausibility check of percentage rows; <c>null</c> to use the page's.</param>
    /// <param name="forcedVoltage">A voltage that overrides the page's (115 or 230), or <c>null</c>.</param>
    /// <param name="efficiencyTolerance">Allowed difference between DC/AC and the printed efficiency, in percentage points.</param>
    public static HtmlReport Parse(string html, int? ratedWattage = null, int? forcedVoltage = null, double efficiencyTolerance = 0.05)
    {
        ArgumentNullException.ThrowIfNull(html);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var metadata = ReadMetadata(document);
        var wattage = ratedWattage ?? metadata.Wattage;

        var points = new List<MeasuredPoint>();
        var warnings = new List<string>();
        foreach (var (table, contextVoltage) in TablesWithContext(document))
        {
            var rows = table.Descendants("tr").Where(r => r.Ancestors("table").FirstOrDefault() == table)
                .Select(r => (Node: r, Cells: r.ChildNodes.Where(c => c.Name is "td" or "th").Select(CellText).ToList()))
                .ToList();
            var paneVoltage = PaneVoltageOf(table);
            var paneKind = PaneKindOf(table);
            int? wattsColumn = null;
            var numbered = false;
            for (var i = 0; i < rows.Count; i++)
            {
                var (node, cells) = rows[i];
                var header = cells.FindIndex(c => c.StartsWith("DC/AC", StringComparison.OrdinalIgnoreCase));
                if (header >= 0)
                {
                    wattsColumn = header;

                    // Older pages (2020) label the rows "Test #" 1..11 / 1..4 instead of 10%..110% / 20W..80W; only
                    // tables with rail columns qualify (the 5VSB table is numbered too).
                    numbered = cells[0].StartsWith("Test #", StringComparison.OrdinalIgnoreCase) && cells.Contains("12V");
                    continue;
                }

                if (cells.Count < 2)
                {
                    continue;
                }

                string label;
                double? lightWatts, percent;
                if (numbered && TestNumberRegex().IsMatch(cells[0]))
                {
                    if (!TryNumberedLabel(int.Parse(cells[0], CultureInfo.InvariantCulture), paneKind, cells, wattage, out label, out lightWatts, out percent))
                    {
                        warnings.Add($"Line {node.Line}: test #{cells[0]} skipped: cannot tell whether it is a light-load or a 10-110% row.");
                        continue;
                    }
                }
                else if (!TryLabel(cells[0], out label, out lightWatts, out percent))
                {
                    continue;
                }

                var hasPairCell = cells.Skip(1).Any(c => PairRegex().IsMatch(c));
                if (wattsColumn is null && !hasPairCell)
                {
                    continue; // Not a load table (e.g. ripple rows "10%" without watts).
                }

                var line = node.Line;
                var nextFirst = i + 1 < rows.Count ? rows[i + 1].Cells.FirstOrDefault() ?? string.Empty : null;
                var next = nextFirst is not null && !TryLabel(nextFirst, out _, out _, out _) && !(numbered && TestNumberRegex().IsMatch(nextFirst))
                    ? rows[i + 1].Cells
                    : null;
                double? nominal = lightWatts ?? (percent is not null && wattage is not null ? percent / 100.0 * wattage : null);
                var efficiency = cells.Skip(1).Select(c => PercentRegex().Match(c)).Where(m => m.Success)
                    .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                    .Where(e => e is > 40 and < 100)
                    .Select(e => (double?)e)
                    .FirstOrDefault();

                if (!TryPair(cells, next, wattsColumn, nominal, efficiency, efficiencyTolerance, out var dc, out var ac, out var problem))
                {
                    warnings.Add($"Line {line}: row {label} skipped: {problem}");
                    continue;
                }

                if (next is not null && !hasPairCell)
                {
                    i++; // The second row of a two-row test (voltages and AC watts) is consumed.
                }

                var voltage = forcedVoltage ?? paneVoltage ?? AcVoltageOf(next ?? cells) ?? AcVoltageOf(cells) ?? contextVoltage;
                if (voltage is null)
                {
                    warnings.Add($"Line {line}: row {label} skipped: no voltage tab, AC voltage cell or 115V/230V heading found.");
                    continue;
                }

                if (problem.Length > 0)
                {
                    warnings.Add($"Line {line}: {voltage}V row {label}: {problem}");
                }

                if (points.Any(p => p.Voltage == voltage && p.Label == label))
                {
                    warnings.Add($"Line {line}: duplicate {voltage}V row {label} ignored (AC {ac}, DC {dc}).");
                    continue;
                }

                points.Add(new MeasuredPoint(voltage.Value, label, ac, dc, line));
            }
        }

        return new HtmlReport(new ParseResult(points, warnings), metadata);
    }

    // Finds DC and AC for one test: a "DC / AC" pair cell, or (two-row layout) the watts cell of the first row and the
    // watts cell of the second row. The DC/AC column of the header row and the printed efficiency pick the cells.
    private static bool TryPair(
        List<string> first,
        List<string>? second,
        int? wattsColumn,
        double? nominal,
        double? efficiency,
        double tolerance,
        out double dc,
        out double ac,
        out string problem)
    {
        dc = ac = 0;
        problem = string.Empty;
        bool Matches(double d, double a) => efficiency is not null && Math.Abs((d / a * 100) - efficiency.Value) <= tolerance;
        bool Plausible(double d, double a) => d > 0 && a > d && d / a >= 0.4 && (nominal is null || Math.Abs(d - nominal.Value) <= 0.2 * nominal.Value);

        var pairCell = first.Skip(1).Select(c => PairRegex().Match(c)).FirstOrDefault(m => m.Success);
        var candidates = new List<(double Dc, double Ac, int Rank)>();
        if (pairCell is not null)
        {
            candidates.Add((Number(pairCell.Groups["dc"].Value), Number(pairCell.Groups["ac"].Value), 0));
        }
        else if (second is not null)
        {
            // The label cell spans both rows, so the second row's cells sit one column to the left.
            for (var c1 = 1; c1 < first.Count; c1++)
            {
                for (var c2 = 0; c2 < second.Count; c2++)
                {
                    if (TryNumber(first[c1], out var d) && TryNumber(second[c2], out var a))
                    {
                        var rank = c1 == wattsColumn && c2 == wattsColumn - 1 ? 0 : c2 == c1 - 1 ? 1 : 2;
                        candidates.Add((d, a, rank));
                    }
                }
            }
        }

        var plausible = candidates.Where(p => Plausible(p.Dc, p.Ac)).ToList();
        var chosen = plausible.Where(p => Matches(p.Dc, p.Ac)).OrderBy(p => p.Rank).Select(p => ((double, double, int)?)p).FirstOrDefault()
            ?? plausible.Where(p => p.Rank <= 1).OrderBy(p => p.Rank).Select(p => ((double, double, int)?)p).FirstOrDefault();
        if (chosen is null)
        {
            problem = pairCell is null && second is null ? "no DC/AC pair cell and no second row with the AC value." : "no plausible DC/AC pair.";
            return false;
        }

        (dc, ac, _) = chosen.Value;
        if (efficiency is not null && !Matches(dc, ac))
        {
            problem = $"DC/AC {dc}/{ac} = {dc / ac * 100:0.###}% does not match the printed efficiency {efficiency:0.###}%; check this row.";
        }

        return true;
    }

    private static IEnumerable<(HtmlNode Table, int? ContextVoltage)> TablesWithContext(HtmlDocument document)
    {
        int? context = null;
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.NodeType == HtmlNodeType.Text && node.ParentNode?.Name is not ("script" or "style"))
            {
                context = VoltageOf(Clean(node.InnerText)) ?? context;
            }
            else if (node.Name == "img" && ImagePathVoltageRegex().Match(node.GetAttributeValue("src", string.Empty)) is { Success: true } img)
            {
                context = int.Parse(img.Groups["v"].Value, CultureInfo.InvariantCulture);
            }
            else if (node.Name == "table")
            {
                yield return (node, context);
            }
        }
    }

    // Test #n is 20n W in a light-load table and 10n % in a 10-110% table. The pane id says which table it is;
    // without one, the row's DC value decides (the nominal load it is within ±20 % of).
    private static bool TryNumberedLabel(int n, string? kind, List<string> cells, int? wattage, out string label, out double? lightWatts, out double? percent)
    {
        label = string.Empty;
        lightWatts = percent = null;
        var light = n is >= 1 and <= 4 ? 20.0 * n : (double?)null;
        var load = n is >= 1 and <= 11 ? 10.0 * n : (double?)null;
        bool Near(double nominal) => cells.Skip(1).Any(c => TryNumber(c, out var v) && Math.Abs(v - nominal) <= 0.2 * nominal);

        var isLight = kind switch
        {
            "light" => light is not null ? true : (bool?)null,
            "load" => load is not null ? false : null,
            _ when light is not null && Near(light.Value) && !(load is not null && wattage is not null && Near(load.Value / 100 * wattage.Value)) => true,
            _ when load is not null && wattage is not null && Near(load.Value / 100 * wattage.Value) => false,
            _ => null,
        };
        if (isLight is null)
        {
            return false;
        }

        if (isLight.Value)
        {
            lightWatts = light;
            label = $"{light:0}W";
        }
        else
        {
            percent = load;
            label = $"{load:0}%";
        }

        return true;
    }

    // Tab pane ids name the table: "nav-<random>-10-110-230" (load tests), "nav-<random>-20-80-115" (light load).
    private static string? PaneKindOf(HtmlNode table) =>
        table.Ancestors()
            .Select(a => a.GetAttributeValue("id", string.Empty))
            .Select(id => id.EndsWith("-115", StringComparison.Ordinal) || id.EndsWith("-230", StringComparison.Ordinal) ? id[..^4] : null)
            .Where(id => id is not null)
            .Select(id => id!.EndsWith("-10-110", StringComparison.Ordinal) ? "load" : id.EndsWith("-20-80", StringComparison.Ordinal) ? "light" : null)
            .FirstOrDefault(k => k is not null);

    // Voltage tab panes have ids ending in "-115" or "-230" (e.g. "nav-x8wj96YiMf-10-110-230").
    private static int? PaneVoltageOf(HtmlNode table) =>
        table.Ancestors()
            .Select(a => PaneIdRegex().Match(a.GetAttributeValue("id", string.Empty)))
            .Where(m => m.Success)
            .Select(m => (int?)int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture))
            .FirstOrDefault();

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static bool TryNumber(string cell, out double value)
    {
        var m = PlainNumberRegex().Match(cell);
        value = m.Success ? Number(m.Groups[1].Value) : 0;
        return m.Success;
    }

    private static PageMetadata ReadMetadata(HtmlDocument document)
    {
        var metadata = new PageMetadata();
        var root = document.DocumentNode;
        metadata.Url = root.Descendants("link")
            .Where(l => l.GetAttributeValue("rel", string.Empty).Equals("canonical", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.GetAttributeValue("href", string.Empty))
            .Concat(root.Descendants("meta")
                .Where(m => m.GetAttributeValue("property", string.Empty).Equals("og:url", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.GetAttributeValue("content", string.Empty)))
            .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

        // A saved page has no canonical link, but its links (PDF download, languages) start with the report page URL.
        metadata.Url ??= root.Descendants("a")
            .Select(a => EvaluationUrlRegex().Match(a.GetAttributeValue("href", string.Empty)))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault();

        // Two-cell rows such as "Brand | Seasonic", "Model | Prime Fanless TX-700", "Rated Power (W) | 700".
        foreach (var row in root.Descendants("tr"))
        {
            var cells = row.ChildNodes.Where(c => c.Name is "td" or "th").Select(CellText).ToList();
            if (cells.Count != 2 || cells[1].Length == 0)
            {
                continue;
            }

            switch (cells[0].ToLowerInvariant())
            {
                case "brand":
                    metadata.Brand ??= cells[1];
                    break;
                case "model" when metadata.Model is null:
                    // "Prime TX-1600 (ATX v3.0)": the platform revision goes to Revision.
                    var split = ModelRevisionRegex().Match(cells[1]);
                    metadata.Model = split.Success ? split.Groups["model"].Value : cells[1];
                    metadata.Revision = split.Success ? split.Groups["rev"].Value : null;
                    break;
                case "testing date":
                    // Only this label: "Date of receipt", "Report Issued Date" and "Certificate Issue Date" are other dates.
                    metadata.TestingDate ??= cells[1];
                    break;
                case "rated power (w)" or "rated power":
                    metadata.Wattage ??= int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0 ? w : null;
                    break;
            }
        }

        var text = Clean(root.InnerText);
        metadata.ReportId = ReportIdRegex().Match(text) is { Success: true } id ? id.Groups[1].Value : null;

        // Ratings: from text or from badge image alt/title/src (e.g. "ETA Titanium", "80 PLUS Gold", "eta_platinum.png").
        var badges = string.Join(' ', root.Descendants("img").SelectMany(i => new[]
        {
            i.GetAttributeValue("alt", string.Empty), i.GetAttributeValue("title", string.Empty), i.GetAttributeValue("src", string.Empty),
        }));
        var haystack = text + " " + badges.Replace('_', ' ').Replace('-', ' ');
        metadata.RatingCybenetics = EtaRegex().Match(haystack) is { Success: true } eta ? Title(eta.Groups["r"].Value) : null;
        metadata.Rating80Plus = EightyPlusRegex().Match(haystack) is { Success: true } plus ? Title(plus.Groups["r"].Value) : null;
        return metadata;
    }

    private static string Title(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());

    private static bool TryLabel(string cell, out string label, out double? lightWatts, out double? percent)
    {
        lightWatts = percent = null;
        label = string.Empty;
        var m = LabelRegex().Match(cell);
        if (!m.Success)
        {
            return false;
        }

        if (m.Groups["w"].Success)
        {
            lightWatts = double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
            label = m.Groups["w"].Value + "W";
        }
        else
        {
            percent = double.Parse(m.Groups["p"].Value, CultureInfo.InvariantCulture);
            label = m.Groups["p"].Value + "%";
        }

        return true;
    }

    private static int? AcVoltageOf(List<string> cells)
    {
        // The AC voltage is the last cell value in volts within mains range; rail voltages (3.3–12 V) never reach it.
        foreach (var cell in Enumerable.Reverse(cells))
        {
            foreach (Match m in VoltsRegex().Matches(cell))
            {
                var volts = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (volts is >= 85 and <= 280)
                {
                    return volts >= 180 ? 230 : 115;
                }
            }
        }

        return null;
    }

    private static int? VoltageOf(string text)
    {
        var found = SectionVoltageRegex().Matches(text).Select(m => int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture)).Distinct().ToList();
        return found.Count == 1 ? found[0] : null;
    }

    private static string CellText(HtmlNode cell) =>
        Clean(string.Join(' ', cell.DescendantsAndSelf().Where(n => n.NodeType == HtmlNodeType.Text).Select(n => n.InnerText)));

    private static string Clean(string text) => WhitespaceRegex().Replace(HtmlEntity.DeEntitize(text), " ").Trim();

    [GeneratedRegex(@"^(?:(?<w>20|40|60|80)\s*W|(?<p>10|20|30|40|50|60|70|80|90|100|110)\s*%)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"^(?<dc>\d+(?:\.\d+)?)\s*W?\s*/\s*(?<ac>\d+(?:\.\d+)?)\s*W?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PairRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*%$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"(?<![\d.])(\d+(?:\.\d+)?)\s*V(?:AC)?(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VoltsRegex();

    [GeneratedRegex(@"^(?<v>115|230)\s*V(?:AC)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionVoltageRegex();

    [GeneratedRegex(@"/(?<v>115|230)/", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePathVoltageRegex();

    [GeneratedRegex(@"^\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TestNumberRegex();

    [GeneratedRegex(@"-(?<v>115|230)$", RegexOptions.CultureInvariant)]
    private static partial Regex PaneIdRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*W?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumberRegex();

    [GeneratedRegex(@"^(https?://[^\s""'#?]*/evaluations/psus/\d+/)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EvaluationUrlRegex();

    [GeneratedRegex(@"^(?<model>.+?)\s*\((?<rev>[^()]*(?:ATX|v\d)[^()]*)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelRevisionRegex();

    [GeneratedRegex(@"Report\s*ID\s*:?\s*(\d{2}PS\d{3,5}[A-Z]?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReportIdRegex();

    [GeneratedRegex(@"\bETA\s*(?:rating\s*)?:?\s*(?<r>Diamond|Titanium|Platinum|Gold|Silver|Bronze|Standard)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EtaRegex();

    [GeneratedRegex(@"\b80\s*(?:PLUS|\+)\s*(?:rating\s*)?:?\s*(?<r>White|Bronze|Silver|Gold|Platinum|Titanium)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EightyPlusRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
