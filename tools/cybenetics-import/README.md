# cybenetics-import

A maintainer tool that turns one Cybenetics PSU report into one `psu-database.json` entry. It reads the
light-load table (20/40/60/80 W) and the 10–110 % load table for each input voltage it finds (230 V and, when the
report has it, 115 V) and writes the (AC W, DC W) pairs sorted by AC.

Rules it follows:

- **One report at a time, downloaded by hand.** The tool never contacts cybenetics.com. There is no bulk scraping.
- **Attribution is mandatory.** Every entry carries `source.name = "Cybenetics"`, the report id and the report URL;
  Cybenetics permits reuse of report data on that condition.
- **Same name, different platform:** if a model name exists in several platform revisions, give each its own `--id`
  and set `--revision`.

## Running it

From the repository root:

```powershell
dotnet run --project tools/cybenetics-import -- C:\Downloads\21PS1872A.pdf `
  --id seasonic-prime-fanless-tx-700 --brand Seasonic --series "Prime Fanless TX" `
  --model "Prime Fanless TX-700" --wattage 700 --rating80plus Titanium --ratingCybenetics Titanium `
  --reportId 21PS1872A --url https://www.cybenetics.com/evaluations/psus/1872/ `
  --out entry.json
```

The entry JSON goes to stdout (or `--out`). A summary and any warnings go to stderr, for example
`230 V: 15 points (10%, ..., 110%, 20W, 40W, 60W, 80W)`. The exit code is 0 on success, 1 when nothing usable was
found or the entry fails the app's database validation, and 2 for bad arguments.

If 230 V and 115 V results come as separate PDFs, pass both: `--input230 eu.pdf --input115 us.pdf`. A plain path
(without `--input230`/`--input115`) takes the voltage from marker lines in the text such as `230V` or `115V`.
Inputs that are not `.pdf` are read as already extracted text, one line per table row.

Paste the entry into the `psus` array of `psu-database.json`, bump `updated` to today's date (the app's
"Check for database update" only installs a file with a newer `updated` date), and run `dotnet test`.

## How the parser works

PdfPig extracts the words of each page and the tool rebuilds text lines from their positions. The parser then walks
the lines:

1. A **voltage marker** line (`230V`, `115 VAC`; not a table row, and not naming both voltages) sets the voltage.
2. A **table header** line starts the light-load table (`Light Load`) or the load table (`Load Regulation`,
   `10-110% Load`, `Efficiency & Load`, `Load Tests`). A **section end** line (`Ripple`, `Hold-up`, `Inrush`,
   `Vampire`, `Protection`, ...) closes it.
3. Inside a table, a line starting with a **row label** (`20W`, `50%`, optionally after a test number) starts a row.
   Up to two following lines belong to the row, because Cybenetics prints DC watts in the row's first line and AC
   watts directly below it.
4. Only numbers without a unit (or with a `W` suffix) are candidates, so currents (`5.450A`), voltages
   (`12.110V`), percentages and temperatures are ignored. **DC** is the candidate on the row line closest to the
   nominal load (20/40/60/80 W, or percentage × `--wattage`), within ±20 %. **AC** is a candidate whose efficiency
   DC/AC is between 50 % and 99.5 %, preferring the value in the same column on the next line, then the next value
   after DC on the same line.
5. The first row per voltage and label wins. Repeated rows (e.g. a table repeated on a summary page) are reported
   and ignored.

`tests/Fixtures/tx700-stacked.txt` is a synthetic text fixture that mimics this layout, built from the TX-700 seed
values in `psu-database.json`. The tests check that the tool reproduces the seed entry exactly, also after a round
trip through a generated PDF.

## When a report does not parse

Run with `--dump-text` to print the extracted, numbered lines to stderr and compare them with the PDF. Then adjust
the locators (all are case-insensitive .NET regular expressions):

| Option | Default | Purpose |
|---|---|---|
| `--voltage-marker` | `(?<![\d.])(?<v>115\|230)\s*V(?:AC)?(?![\w.])` | Voltage switch; group `v` = 115 or 230 |
| `--light-load-header` | `light[\s-]*load` | Starts the light-load table |
| `--load-header` | `load\s*regulation\|10\s*[-–~]\s*110\s*%\|efficiency\s*(?:&\|and)\s*load\|load\s*tests?\b` | Starts the 10–110 % table |
| `--section-end` | `ripple\|hold[\s-]*up\|inrush\|vampire\|standby\s*power\|cross[\s-]*load\|protection\|power\s*ok\|fan\s*noise\s*(?:test\|graph)` | Ends the current table |
| `--light-load-row` | `^\s*(?:#?\d{1,2}\s+)?(?<w>20\|40\|60\|80)\s*W(?![\w.])` | Light-load row label; group `w` = watts |
| `--load-row` | `^\s*(?:#?\d{1,2}\s+)?(?<p>10\|20\|...\|110)\s*%(?![\w.])` | Load row label; group `p` = percent |
| `--dc-tolerance` | `0.2` | Allowed deviation of DC from the nominal load |
| `--voltage` | none | Voltage for rows that appear before any marker |

## What to check by hand before committing an entry

The parser has only been tested against synthetic text that imitates the report layout, so check every new entry
against the PDF:

- **Point count.** Normally 15 per voltage: 4 light-load rows plus 11 load rows. The tool warns about missing
  labels. If the 10 % row (or the 80 W row) is missing, look at `--dump-text`.
- **Spot-check values.** Compare at least the 20 W, 10 %, 50 % and 110 % rows for each voltage with the PDF: DC from
  the "DC/AC (Watts)" column's first line and AC from its second line. Make sure AC was not taken from another
  column, e.g. the AC voltage (around 230 or 115) or the power factor.
- **Voltage assignment.** Make sure the 230 V and 115 V curves are not swapped: at the same load the 115 V AC value
  is normally the higher one.
- **Efficiency sanity.** DC/AC should rise from light load to a peak around 40–60 % load and fall slightly at
  110 %, consistent with the report's efficiency column.
- **Metadata.** Model, revision/platform, wattage, 80 Plus and Cybenetics ratings, report id and URL must match
  the report.
