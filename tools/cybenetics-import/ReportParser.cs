using System.Globalization;
using System.Text.RegularExpressions;

namespace CybeneticsImport;

/// <summary>One measured operating point.</summary>
/// <param name="Voltage">The input voltage (115 or 230).</param>
/// <param name="Label">The row label, e.g. <c>20W</c> or <c>50%</c>.</param>
/// <param name="Ac">AC input power in watts.</param>
/// <param name="Dc">DC output power in watts.</param>
/// <param name="SourceLine">The 1-based line number of the row label, for diagnostics.</param>
public sealed record MeasuredPoint(int Voltage, string Label, double Ac, double Dc, int SourceLine);

/// <summary>The result of parsing one report's text.</summary>
/// <param name="Points">The accepted points, in document order.</param>
/// <param name="Warnings">Rows that were skipped or ambiguous.</param>
public sealed record ParseResult(IReadOnlyList<MeasuredPoint> Points, IReadOnlyList<string> Warnings)
{
    /// <summary>Returns the (AC, DC) pairs per voltage, sorted by AC.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<(double Ac, double Dc)>> Curves =>
        Points
            .GroupBy(p => p.Voltage)
            .OrderByDescending(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(double, double)>)g.OrderBy(p => p.Ac).Select(p => (p.Ac, p.Dc)).ToList());
}

/// <summary>
/// The configurable locators the parser uses. Every pattern is a .NET regular expression matched case-insensitively
/// against one extracted text line.
/// </summary>
public sealed class ParserOptions
{
    /// <summary>Gets or sets the pattern of a line that starts the light-load table (20/40/60/80 W rows).</summary>
    public string LightLoadHeader { get; set; } = @"light[\s-]*load";

    /// <summary>Gets or sets the pattern of a line that starts the 10–110 % load table.</summary>
    public string LoadTableHeader { get; set; } = @"load\s*regulation|10\s*[-–~]\s*110\s*%|efficiency\s*(?:&|and)\s*load|load\s*tests?\b";

    /// <summary>Gets or sets the pattern of a line that ends the current table (a following, unrelated section).</summary>
    public string SectionEnd { get; set; } = @"ripple|hold[\s-]*up|inrush|vampire|standby\s*power|cross[\s-]*load|protection|power\s*ok|fan\s*noise\s*(?:test|graph)";

    /// <summary>
    /// Gets or sets the pattern of a line that switches the input voltage; group <c>v</c> must capture 115 or 230.
    /// Only checked on lines that are not table rows. A line naming both voltages is ignored.
    /// </summary>
    public string VoltageMarker { get; set; } = @"(?<![\d.])(?<v>115|230)\s*V(?:AC)?(?![\w.])";

    /// <summary>Gets or sets the pattern of a light-load row label; group <c>w</c> captures the nominal DC watts.</summary>
    public string LightLoadRow { get; set; } = @"^\s*(?:#?\d{1,2}\s+)?(?<w>20|40|60|80)\s*W(?![\w.])";

    /// <summary>Gets or sets the pattern of a load-table row label; group <c>p</c> captures the load percentage.</summary>
    public string LoadRow { get; set; } = @"^\s*(?:#?\d{1,2}\s+)?(?<p>10|20|30|40|50|60|70|80|90|100|110)\s*%(?![\w.])";

    /// <summary>
    /// Gets or sets the pattern of a line that is skipped while collecting a row's continuation lines (page footers
    /// and repeated column headers that a page break can put between a row's DC line and its AC line).
    /// </summary>
    public string IgnoreLine { get; set; } = @"\bpage\s*\d+\s*(?:/|of)\s*\d+\b|^\s*test\b";

    /// <summary>Gets or sets how far the DC value may deviate from the nominal load (fraction, e.g. 0.2 = ±20 %).</summary>
    public double DcTolerance { get; set; } = 0.2;

    /// <summary>
    /// Gets or sets how far (in percentage points) DC/AC may differ from an efficiency percentage printed on the row
    /// for the pair to count as confirmed by it.
    /// </summary>
    public double EfficiencyTolerance { get; set; } = 0.05;

    /// <summary>Gets or sets the lowest plausible efficiency DC/AC.</summary>
    public double MinEfficiency { get; set; } = 0.5;

    /// <summary>Gets or sets the highest plausible efficiency DC/AC.</summary>
    public double MaxEfficiency { get; set; } = 0.995;

    /// <summary>Gets or sets how many lines after a row label belong to the same row (stacked DC/AC layout).</summary>
    public int MaxContinuationLines { get; set; } = 2;

    /// <summary>Gets or sets the voltage assumed before any voltage marker; <c>null</c> skips such rows.</summary>
    public int? DefaultVoltage { get; set; }

    /// <summary>Gets or sets a voltage that overrides all markers (for a report that covers one voltage).</summary>
    public int? ForcedVoltage { get; set; }
}

/// <summary>
/// Extracts (AC, DC) points from the text lines of a Cybenetics report.
/// </summary>
/// <remarks>
/// <para>
/// The parser walks the lines once. Voltage marker lines (e.g. "230V EU", "115V") set the current voltage; table
/// header lines set the current table (light load or 10–110 % load) until another header or a section-end line.
/// Inside a table, a line starting with a row label ("20W", "50%", optionally after a test number) starts a row; the
/// row also includes up to <see cref="ParserOptions.MaxContinuationLines"/> following lines that are not labels or
/// headers, because Cybenetics prints DC watts on the first line of a row and AC watts directly below it.
/// </para>
/// <para>
/// Numbers without a unit suffix (or with a "W" suffix) are candidates. DC candidates lie on the row line within
/// <see cref="ParserOptions.DcTolerance"/> of the nominal load (20/40/60/80 W, or percentage × rated wattage); AC
/// candidates have a plausible DC/AC ratio. When the row prints an efficiency percentage, the (DC, AC) pair whose
/// ratio matches it is taken, so other numbers near the nominal load (fan speed, noise) cannot be mistaken for DC.
/// Otherwise DC is the candidate closest to the nominal load and AC the value in the same column on the next line
/// (stacked layout), then the next candidate after DC on the same line (side-by-side layout). Page footers and
/// repeated column headers between a row's lines are skipped (<see cref="ParserOptions.IgnoreLine"/>). The first
/// accepted row per voltage and label wins; later duplicates are reported as warnings.
/// </para>
/// </remarks>
public sealed partial class ReportParser
{
    private readonly ParserOptions _options;
    private readonly Regex _lightHeader;
    private readonly Regex _loadHeader;
    private readonly Regex _sectionEnd;
    private readonly Regex _voltageMarker;
    private readonly Regex _lightRow;
    private readonly Regex _loadRow;
    private readonly Regex _ignoreLine;

    /// <summary>Creates a parser.</summary>
    /// <param name="options">Locators and tolerances; defaults when <c>null</c>.</param>
    public ReportParser(ParserOptions? options = null)
    {
        _options = options ?? new ParserOptions();
        const RegexOptions flags = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        _lightHeader = new Regex(_options.LightLoadHeader, flags);
        _loadHeader = new Regex(_options.LoadTableHeader, flags);
        _sectionEnd = new Regex(_options.SectionEnd, flags);
        _voltageMarker = new Regex(_options.VoltageMarker, flags);
        _lightRow = new Regex(_options.LightLoadRow, flags);
        _loadRow = new Regex(_options.LoadRow, flags);
        _ignoreLine = new Regex(_options.IgnoreLine, flags);
    }

    private enum Table
    {
        None,
        LightLoad,
        Load,
    }

    /// <summary>Parses report text.</summary>
    /// <param name="lines">The text lines, in reading order.</param>
    /// <param name="ratedWattage">The PSU's rated DC wattage (to locate the DC value of percentage rows).</param>
    public ParseResult Parse(IReadOnlyList<string> lines, int ratedWattage)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratedWattage);

        var points = new List<MeasuredPoint>();
        var warnings = new List<string>();
        var seen = new HashSet<(int, string)>();
        var table = Table.None;
        int? voltage = _options.ForcedVoltage ?? _options.DefaultVoltage;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;
            var row = table == Table.LightLoad ? _lightRow.Match(line) : table == Table.Load ? _loadRow.Match(line) : Match.Empty;
            if (!row.Success)
            {
                table = UpdateState(line, table, ref voltage);
                continue;
            }

            var label = table == Table.LightLoad ? row.Groups["w"].Value + "W" : row.Groups["p"].Value + "%";
            var nominal = table == Table.LightLoad
                ? double.Parse(row.Groups["w"].Value, CultureInfo.InvariantCulture)
                : double.Parse(row.Groups["p"].Value, CultureInfo.InvariantCulture) / 100.0 * ratedWattage;

            var block = new List<string> { line[(row.Index + row.Length)..] };
            for (var j = i + 1; j < lines.Count && block.Count <= _options.MaxContinuationLines; j++)
            {
                var next = lines[j] ?? string.Empty;
                if (IsBoundary(next))
                {
                    break;
                }

                if (!_ignoreLine.IsMatch(next))
                {
                    block.Add(next);
                }
            }

            if (voltage is null)
            {
                warnings.Add($"Line {i + 1}: row {label} skipped because no voltage marker was seen yet (pass --voltage).");
                continue;
            }

            if (!TryReadRow(block, nominal, out var ac, out var dc, out var problem))
            {
                warnings.Add($"Line {i + 1}: {voltage}V row {label} skipped: {problem}");
                continue;
            }

            if (problem.Length > 0)
            {
                warnings.Add($"Line {i + 1}: {voltage}V row {label}: {problem}");
            }

            if (!seen.Add((voltage.Value, label)))
            {
                warnings.Add($"Line {i + 1}: duplicate {voltage}V row {label} ignored (AC {ac}, DC {dc}).");
                continue;
            }

            points.Add(new MeasuredPoint(voltage.Value, label, ac, dc, i + 1));
        }

        return new ParseResult(points, warnings);
    }

    /// <summary>Splits a line into whitespace-separated tokens.</summary>
    /// <param name="text">The text.</param>
    internal static string[] Tokens(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Parses a plain number token ("69.997" or "69.997W"); unit-suffixed tokens are not candidates.</summary>
    /// <param name="token">The token.</param>
    /// <param name="value">The value.</param>
    internal static bool TryPlainNumber(string token, out double value)
    {
        var m = PlainNumberRegex().Match(token);
        value = m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return m.Success;
    }

    private bool IsBoundary(string line) =>
        _lightRow.IsMatch(line) || _loadRow.IsMatch(line) || _lightHeader.IsMatch(line) || _loadHeader.IsMatch(line) ||
        _sectionEnd.IsMatch(line) || VoltageOf(line) is not null;

    private Table UpdateState(string line, Table table, ref int? voltage)
    {
        if (VoltageOf(line) is int v && _options.ForcedVoltage is null)
        {
            voltage = v;
        }

        if (_lightHeader.IsMatch(line))
        {
            return Table.LightLoad;
        }

        if (_loadHeader.IsMatch(line))
        {
            return Table.Load;
        }

        return _sectionEnd.IsMatch(line) ? Table.None : table;
    }

    private int? VoltageOf(string line)
    {
        var found = _voltageMarker.Matches(line)
            .Select(m => int.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    private bool TryReadRow(List<string> block, double nominal, out double ac, out double dc, out string problem)
    {
        ac = dc = 0;
        var cells = new List<Cell>();
        var efficiencies = new List<double>();
        for (var l = 0; l < block.Count; l++)
        {
            var tokens = Tokens(block[l]);
            for (var c = 0; c < tokens.Length; c++)
            {
                var spacedPercent = c + 1 < tokens.Length && tokens[c + 1] == "%";
                if (TryPlainNumber(tokens[c], out var value) && !spacedPercent)
                {
                    cells.Add(new Cell(l, c, value));
                }
                else if ((TryPercent(tokens[c], out var percent) || (spacedPercent && TryPlainNumber(tokens[c], out percent))) &&
                         percent >= _options.MinEfficiency * 100 && percent <= _options.MaxEfficiency * 100)
                {
                    efficiencies.Add(percent);
                }
            }
        }

        var dcCells = cells
            .Where(x => x.Line == 0 && Math.Abs(x.Value - nominal) <= _options.DcTolerance * nominal)
            .OrderBy(x => Math.Abs(x.Value - nominal))
            .ToList();
        if (dcCells.Count == 0)
        {
            problem = $"no DC value near {nominal:0.#} W on the row line.";
            return false;
        }

        // Every plausible (DC, AC) pair, best layout position first.
        var pairs = dcCells
            .SelectMany(d => cells
                .Where(a => a != d && a.Value > 0)
                .Where(a => d.Value / a.Value >= _options.MinEfficiency && d.Value / a.Value <= _options.MaxEfficiency)
                .Select(a => (Dc: d, Ac: a, Rank: PositionRank(d, a, cells))))
            .Where(p => p.Rank < int.MaxValue)
            .ToList();

        // A printed efficiency that matches DC/AC identifies the pair regardless of which other numbers are nearby.
        var confirmed = pairs
            .Where(p => efficiencies.Any(e => Math.Abs((p.Dc.Value / p.Ac.Value * 100) - e) <= _options.EfficiencyTolerance))
            .OrderBy(p => p.Rank)
            .ThenBy(p => Math.Abs(p.Dc.Value - nominal))
            .ToList();
        if (confirmed.Count > 0)
        {
            dc = confirmed[0].Dc.Value;
            ac = confirmed[0].Ac.Value;
            problem = string.Empty;
            return true;
        }

        // No efficiency to confirm with: DC closest to nominal, AC by position.
        var dcCell = dcCells[0];
        var chosen = pairs.Where(p => p.Dc == dcCell).OrderBy(p => p.Rank).Select(p => (Cell?)p.Ac).FirstOrDefault();
        if (chosen is null)
        {
            problem = $"DC {dcCell.Value} found but no AC value with a plausible efficiency.";
            return false;
        }

        dc = dcCell.Value;
        ac = chosen.Value.Value;
        problem = efficiencies.Count == 0
            ? string.Empty
            : $"DC/AC {dc}/{ac} = {dc / ac * 100:0.###}% does not match the printed efficiency ({string.Join(", ", efficiencies.Select(e => e.ToString("0.###", CultureInfo.InvariantCulture) + "%"))}); check this row.";
        return true;
    }

    /// <summary>Parses a percentage token ("94.412%").</summary>
    /// <param name="token">The token.</param>
    /// <param name="value">The value in percent.</param>
    internal static bool TryPercent(string token, out double value)
    {
        var m = PercentRegex().Match(token);
        value = m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return m.Success;
    }

    // 0/1: AC in DC's column (±1) on the next line (stacked); 2+: a number after DC on the same line, nearest first
    // (side by side); int.MaxValue: anywhere else (not accepted).
    private static int PositionRank(Cell dc, Cell ac, List<Cell> cells)
    {
        if (ac.Line == dc.Line + 1 && Math.Abs(ac.Column - dc.Column) <= 1)
        {
            return Math.Abs(ac.Column - dc.Column);
        }

        if (ac.Line == dc.Line && ac.Column > dc.Column)
        {
            var between = cells.Count(x => x.Line == dc.Line && x.Column > dc.Column && x.Column < ac.Column);
            return 2 + between;
        }

        return int.MaxValue;
    }

    private readonly record struct Cell(int Line, int Column, double Value);

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)%$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)W?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumberRegex();
}
