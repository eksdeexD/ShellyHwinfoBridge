# cybenetics-import

A maintainer tool that turns one Cybenetics PSU report into one `psu-database.json` entry. It reads the
light-load table (20/40/60/80 W) and the 10–110 % load table for each input voltage it finds (230 V and, when the
report has it, 115 V) and writes the (AC W, DC W) pairs sorted by AC.

Rules it follows:

- **Curated, not crawled.** Imports read pages and PDFs that are already on disk. Only the `download` command makes
  HTTP requests, and only for the `reportUrl`s listed in a reviewed catalogue (see below), within Cybenetics' written
  permission of 2026-11-02.
- **Attribution is mandatory.** Every entry carries `source.name = "Cybenetics"`, the report id, the report URL and
  the report date; Cybenetics permits reuse of the efficiency data on those conditions (see `PSU-DATA-NOTICE.md`).
- **ETA grades are copied from the report**, per input voltage, into `ratingsCybenetics`; units without a published
  ETA rating get none.
- **Same name, different platform:** if a model name exists in several platform revisions, give each its own `--id`
  and set `--revision`.

## Running it

From the repository root:

```powershell
dotnet run --project tools/cybenetics-import -- C:\Downloads\21PS1872A.pdf `
  --id seasonic-prime-fanless-tx-700 --brand Seasonic --series "Prime Fanless TX" `
  --model "Prime Fanless TX-700" --wattage 700 --rating80plus Titanium --eta230 Titanium --eta115 Titanium `
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
link, or the page's own `/evaluations/psus/<n>/` links such as the PDF download) and the revision tags written in
parentheses after the model: an ATX version (`(ATX v3.1)`, also `(ATXv3.1)`, `(ATX 3.1)`, `(ATX3.1)`, normalised to
`ATX v3.1`), a sample number (`(#2)`), a year (`(2024)`), a market (`(EU)`, `(US)`) or the build's manufacturer
(`(HKC)`, `(CE-Link)`, `(CWT)`, `(Great Wall)`). `Prime TX-1600 (ATX v3.0)` becomes model `Prime TX-1600`, revision
`ATX v3.0`; `HX1200i (ATX v3.1) (#2)` becomes `HX1200i`, `ATX v3.1 (#2)`. Other parenthesised text (a sister model such
as `(GX Gold 1050)`, `(1000W)`, `(Black/White)`) stays in the model. Without `--id` the id is derived from brand, model and revision. Command-line (or catalogue) values win. The page names neither the
80 Plus rating nor the series, so pass those.

The Cybenetics ETA grade per voltage (`ratingsCybenetics`, `--eta230`/`--eta115` override it) comes from the page's
ETA badge table near the top: a header row `| 115V | 230V` and a row `Scheme: ETA-EMI PSU | <badge> | <badge>`. A
voltage the unit was not tested at has class `d-none` on both cells; a tested voltage without an ETA rating has an
empty cell (e.g. the 230 V cell of the SuperNOVA 1000 G5). The page prints no grade as text: each badge is an image
named after a number (`17.png`, saved by the browser as `17_kQ_U.png` or `36_002_qx62.png`). The numbers per voltage
are one ladder, matched to grades on the 386 saved pages of 2026-11-02 (the average efficiency of the units falls
steadily down the ladder; the Titanium TX-700, 21PS1872A, has 15 and 27):

| Grade | 115 V badge | 230 V badge |
|---|---|---|
| Titanium | 15 | 27 |
| Platinum | 16 | 28 |
| Gold | 17 | 29 |
| Silver | 18 | 30 |
| Bronze | 49 | 50 |

A badge whose `alt`/`title` names the grade ("ETA Gold") is read from that text. Any other number (Diamond and
Standard have not been seen yet) gives no grade and a warning: add it to `EtaBadges` in `HtmlReportParser.cs` after
checking the report, never guess.

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
is read too. Rows that sit in a tab pane without a `<table>` start tag (the 230 V light-load pane of unit 236 starts with
`</tr><tr>`) are read like a table's rows. Report ids with a two-digit number (`19PS57A`, unit 57) are read too.

Checked against six saved pages (2020–2025, including the 2020 numbered layout): all 15 points per voltage, and the
TX-700 page gives exactly the seed entry, identical to the PDF import of 21PS1872A. Some pages print a light-load
efficiency that differs slightly from DC/AC (e.g. 75.53 % for 20.008/26.422 W); those rows get a warning.

## Batch import

A catalogue is a CSV file with the header
`id,brand,series,model,revision,wattage,rating80Plus,reportId,reportUrl,tier,notes`
(RFC 4180 quoting; `revision`, `tier` and `notes` may be empty; `reportUrl` becomes `source.url` and is what
`download` fetches). Other columns are ignored, so older catalogues with `ratingCybenetics` and `pdfUrl` still load;
the ETA grades come from the report pages, not from the catalogue. Pages and PDFs are named after the report id:

```powershell
dotnet run --project tools/cybenetics-import -- batch --catalog catalog.csv --reports work\reports --out work\entries
```

For each row the tool imports `<reports>\<reportId>.htm` or `.html` (a saved evaluation page) when it exists, otherwise
`<reports>\<reportId>.pdf`; with a page, empty catalogue cells (brand, model, revision, wattage, reportUrl) are filled
from it. A placeholder `reportId` of the form `psu-<unit number>` (fetch lists built from the overview page, which has
no report ids) names the file only: `source.reportId` is the id printed on the page, or is left out with a warning when
the page has none. With an empty `revision`, the revision tags of the model text (the catalogue's, else the page's)
become the revision and leave the model. If `<reportId>-115.pdf` exists next to a PDF, that file is read as 115 V and the main file as 230 V (its own
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

## Backfilling report dates and ETA grades

```powershell
dotnet run --project tools/cybenetics-import -- backfill --pages work\reports\processed --into psu-database.json [--dry-run]
```

Fills `source.reportDate` and `ratingsCybenetics` of existing entries from saved evaluation pages (`*.htm`,
`*.html`); `backfill-dates` and `backfill-eta` do only one of the two. A page matches an entry when it is named
`<reportId>.htm`/`.html` or when the report id printed on it equals `source.reportId`; entries without a report id
match on the report URL. Nothing else changes (not `updated` either); the file must be in the layout `merge` writes.

- **Dates:** entries that already have a date, have no matching page, or whose pages give no date, different dates
  or another report URL are left untouched; the summary lists them.
- **ETA grades:** always taken from the pages, replacing what the entry had (including the single
  `ratingCybenetics` of databases before 1.0.1, which is read as the 230 V grade and reported as `changed:` when the
  page says otherwise). A page whose badge table shows no grade at any voltage removes `ratingsCybenetics`
  (`no ETA rating:`). Entries with no matching page, no badge table, an unknown badge or pages that disagree are left
  untouched and listed. `backfill-dates` refuses a database that still has the old `ratingCybenetics`, because
  rewriting it would change the ETA lines; run `backfill` or `backfill-eta` first.

The run of 2026-11-02 over the 386 saved pages filled all 378 entries: 377 with grades (44 of them with different
grades at 115 V and 230 V; 4 graded at 115 V only and 13 at 230 V only) and one without an ETA rating (EVGA 430W,
20PS1756A). Seven of the 35 single grades of 1.0.0 did not match the 230 V badge and were corrected from the pages
(CX750M, RM1000x, RM650e, RM750e, RM750x Shift, RM850x Shift, Rebel P10 650).

## Downloading report pages

```powershell
# Day one: up to 150 pages (the default cap), in batches of 50 with a 15-minute pause in between.
dotnet run --project tools/cybenetics-import -- download --catalog catalog.csv --reports work\reports
# Day two: the rest. The state file still knows what is saved; already saved pages are skipped.
dotnet run --project tools/cybenetics-import -- download --catalog catalog.csv --reports work\reports --max-per-day 300
```

Downloads each row's `reportUrl` (the public report page; https only, nothing but the listed URLs) to
`<reports>\<reportId>.htm` when neither `<reportId>.htm` nor `<reportId>.html` exists yet. This is what Cybenetics'
written permission of 2026-11-02 covers ("slow down and spread over several days"), and what the importer reads.
The tool never downloads PDFs.

- **One request at a time, at least 15 seconds apart**, also across runs (the time of the last request is kept).
- **User-Agent** `ShellyHwinfoBridge-import/1.0 (+https://github.com/eksdeexD/ShellyHwinfoBridge; zapador@zapador.net)`.
- **Batches:** after every `--batch-size` requests (default 50) the run pauses for `--batch-pause` (default `15m`;
  also `900s`, `1h`, or plain seconds) and prints when it resumes. The 15-second interval applies within a batch.
- **Daily cap:** at most `--max-per-day` requests (default 150; a positive number, there is no "unlimited") per local
  calendar date. `download-state.json` in the reports folder counts the requests per date, so a rerun the same day
  continues from that count and the count starts again after local midnight. When the cap is reached the run stops
  with a message naming the next allowed day (exit code 0).
- **No retries:** a response other than 200, or an HTML page without the report tables and the "Testing Date" row
  (a challenge or block page), stops the run at once, says so, saves nothing for that row and makes no further
  request (exit code 1). Rerun it later, on another day if need be; the state file and the saved pages carry over.

Only run `download` for the units on the list sent to Cybenetics (the reviewed catalogue), and never from an AI agent.

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
- **Metadata.** Model, revision/platform, wattage, 80 Plus rating, the ETA grade per voltage, report id, URL and
  date must match the report.
