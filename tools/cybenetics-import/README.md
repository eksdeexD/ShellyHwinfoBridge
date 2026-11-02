# cybenetics-import

A maintainer tool that turns one Cybenetics PSU report into one `psu-database.json` entry. It reads the
light-load table (20/40/60/80 W) and the 10–110 % load table for each input voltage it finds (230 V and, when the
report has it, 115 V) and writes the (AC W, DC W) pairs sorted by AC.

Rules it follows:

- **Curated, not crawled.** Imports read PDFs that are already on disk. Only the `download` command makes HTTP
  requests, and only for the `pdfUrl`s listed in a reviewed catalogue (see below).
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

`--url` is the report page (e.g. `https://www.cybenetics.com/evaluations/psus/1872/`), not the PDF; it must be an
absolute http(s) URL. Every entry is checked with the app's own database validator before it is written. If only
one voltage is found, the tool warns, because most reports cover both.

To add entries, use `merge` (below) rather than editing `psu-database.json` by hand.

## Saved evaluation pages (HTML)

The report web page (e.g. `https://www.cybenetics.com/evaluations/psus/1872/`) carries the same test tables as the
PDF. Save it from the browser ("Web page, HTML only" is enough) and import it:

```powershell
dotnet run --project tools/cybenetics-import -- import --html "Seasonic Prime TX-1600 (ATX v3.0).htm" `
  --series "Prime TX" --rating80plus Titanium
```

A plain path ending in `.html`/`.htm`, or any non-PDF file that starts with `<html` or `<!doctype html`, is read the
same way. From the page the tool takes the brand, model, wattage (`Rated Power (W)`), report id, report URL (canonical
link, or the page's own `/evaluations/psus/<n>/` links such as the PDF download) and a platform revision written as a
model suffix (`Prime TX-1600 (ATX v3.0)` becomes model `Prime TX-1600`, revision `ATX v3.0`); without `--id` the id
is derived from brand, model and revision. Command-line (or catalogue) values win. The page does not name the
80 Plus or Cybenetics ratings in text (they are badge images), and not the series, so pass those.

The report date (`source.reportDate`, yyyy-MM-dd) is the page's `Testing Date` row ("Jul 16, 2021" becomes
`2021-07-16`). A page carries other dates too: `Certificate Issue Date` at the top, and `Date of receipt` and
`Report Issued Date` beside the testing date; those are not used. `--reportDate` (yyyy-MM-dd or "Jul 16, 2021")
overrides it, and gives PDF imports a date; without one the entry has no `reportDate`.

How it reads the tables (HtmlAgilityPack): in every table with a `DC/AC (Watts)` header, a test is two rows: the
first (label cell `10%`…`110%` or `20W`…`80W` spanning both rows, currents, **DC** watts, efficiency) and the
second (voltages, **AC** watts, `115.2V`). Older pages (2020) number the tests (`Test #` 1–11 and 1–4); those are
mapped to 10–110 % and 20–80 W from the tab pane id, or else from the DC value. Cross-load rows (`CL1`…), ripple
and 5VSB tables never match. The voltage comes from the tab pane id (`nav-…-10-110-230`, `nav-…-20-80-115`), else the
AC voltage cell (≥ 180 V is 230 V), else the nearest `115V`/`230V` heading. The printed efficiency confirms each
pair; a mismatch keeps the row (from the DC/AC column) with a warning. A one-row layout with a `70.006 / 78.495` cell
is read too.

Checked against six saved pages (2020–2025, including the 2020 numbered layout): all 15 points per voltage, and the
TX-700 page gives exactly the seed entry, identical to the PDF import of 21PS1872A. Some pages print a light-load
efficiency that differs slightly from DC/AC (e.g. 75.53 % for 20.008/26.422 W); those rows get a warning.

## Batch import

A catalogue is a CSV file with the header
`id,brand,series,model,revision,wattage,rating80Plus,ratingCybenetics,reportId,reportUrl,pdfUrl,tier,notes`
(RFC 4180 quoting; `revision`, `ratingCybenetics`, `pdfUrl`, `tier` and `notes` may be empty; `reportUrl` becomes
`source.url`). PDFs are named after the report id:

```powershell
dotnet run --project tools/cybenetics-import -- batch --catalog catalog.csv --reports work\reports --out work\entries
```

For each row the tool imports `<reports>\<reportId>.html` (a saved evaluation page) when it exists, otherwise
`<reports>\<reportId>.pdf`; with a page, empty catalogue cells (brand, model, revision, wattage, reportUrl) are filled
from it. If `<reportId>-115.pdf` exists next to a PDF, that file is read as 115 V and the main file as 230 V (its own
voltage markers still win). One `<id>.json` per imported row and a
`summary.csv` (`id,reportId,status,points230,points115,warnings,message`, status `ok` or `error`) go to `--out`.
The exit code is 0 only when every row imported. Rows with warnings are still `ok`: read the `message` column and
check those rows against the PDF. `id` and `reportId` may only contain letters, digits, `.`, `_` and `-`.

## Merging into the database

```powershell
dotnet run --project tools/cybenetics-import -- merge --entries work\entries --into psu-database.json
```

Every `*.json` in `--entries` is merged: an entry with an existing id replaces it, the list is sorted by brand and
model, and `updated` is set to today (`--date yyyy-MM-dd` overrides). The result is validated with the app's
database validator first; on any error nothing is written. Then run `dotnet test`.

## Backfilling report dates

```powershell
dotnet run --project tools/cybenetics-import -- backfill-dates --pages work\reports\processed --into psu-database.json [--dry-run]
```

Fills `source.reportDate` of existing entries from saved evaluation pages (`*.htm`, `*.html`). A page matches an
entry when it is named `<reportId>.htm`/`.html` or when the report id printed on it equals `source.reportId`;
entries without a report id match on the report URL. Entries that already have a date, have no matching page, or
whose pages give no date, different dates or another report URL are left untouched; the summary lists them.
Nothing else changes (not `updated` either); the file must be in the layout `merge` writes.

## Downloading reports

```powershell
dotnet run --project tools/cybenetics-import -- download --catalog catalog.csv --reports work\reports
```

Downloads `pdfUrl` to `<reports>\<reportId>.pdf` for rows whose file does not exist yet: one request at a time, at
least 5 seconds apart, User-Agent `ShellyHwinfoBridge-import/0.1 (+https://github.com/eksdeexD/ShellyHwinfoBridge)`,
https only, nothing but the listed URLs. A response that is not a PDF is not saved; a 401, 403 or 429 stops the run.

**Check the site's terms first.** Cybenetics' database pages state that automated data collection, including by
AI agents, requires their prior written permission. Do not run `download` against cybenetics.com without that
permission; download the PDFs by hand into the reports folder instead (the import commands never go online).

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
   Page footers (`... Page 15 / 19`) and repeated column headers (`Test 12V 5V ...`) between a row's lines are
   skipped, so a page break inside a row does not lose its AC value.
4. Only numbers without a unit (or with a `W` suffix) are candidates, so currents (`5.450A`), voltages
   (`12.110V`), percentages and temperatures are ignored. **DC** candidates are on the row line within ±20 % of the
   nominal load (20/40/60/80 W, or percentage × `--wattage`); **AC** candidates give an efficiency DC/AC between
   50 % and 99.5 %.
5. When the row prints an efficiency (`94.412%`), the (DC, AC) pair whose ratio matches it within 0.05 percentage
   points is taken. This keeps fan speed (e.g. `426` RPM on a 50 % row of an 850 W unit) or PSU noise (`20.1` dB(A)
   on the 20 W row) from being read as DC. Without a matching efficiency, DC is the candidate closest to the nominal
   load and AC the value in the same column on the next line, then the next value after DC on the same line; if a
   printed efficiency disagrees, the row is kept with a warning.
6. The first row per voltage and label wins. Repeated rows (e.g. a table repeated on a summary page) are reported
   and ignored.

Layout of real reports (checked against 21PS1872A, issued 2021): the 10–110 % table is "Typical Load Tests" under
"Supplementary Tests (For Reference)", followed by "Light Load Tests"; each table has a `115V` block and a `230V`
block, with the voltage line *after* the table title; each row is a DC line (`10% <amps...> 70.914 90.014% ...`)
and an AC line (`12.140V ... 78.781 ... 230.41V`); crossload rows (`CL1`–`CL4`) follow 110 %; trailing zeros are
dropped (`210.01`); the performance summary has lines such as `12V Load Regulation 0.45%`, which open a load table
that the next section end closes again without rows.

Fixtures (synthetic text, not copied from reports):

- `tests/Fixtures/tx700-stacked.txt`: the stacked layout built from the TX-700 seed values in `psu-database.json`;
  the tests check that the tool reproduces the seed entry exactly, also after a round trip through a generated PDF.
- `tests/Fixtures/evaluation-page-tx700.html`: a synthetic evaluation page with the structure described above
  (tab panes, two-row tests, cross-load, ripple and 5VSB tables) carrying the seed DC/AC values.
- `tests/Fixtures/supplementary-fan-pagebreak.txt`: the real-report layout above for a fictitious 850 W unit, with
  fan speed and noise columns whose values sit closer to the nominal load than DC, a page break inside a row,
  crossload rows, a 5VSB efficiency table and a summary block.

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
| `--ignore-line` | `\bpage\s*\d+\s*(?:/\|of)\s*\d+\b\|^\s*test\b` | Lines skipped inside a row (footers, repeated headers) |
| `--dc-tolerance` | `0.2` | Allowed deviation of DC from the nominal load |
| `--voltage` | none | Voltage for rows that appear before any marker |

## What to check by hand before committing an entry

The parser reproduces the seed entry exactly from the real 21PS1872A PDF, but has not yet seen older (2018–2020)
or the newest (2024–2025) report layouts, so check every new entry against the PDF:

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
