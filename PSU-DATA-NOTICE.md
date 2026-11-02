# Notice for the PSU efficiency data

The PSU efficiency measurements in `psu-database.json` (the `curves` of every entry whose `source.name` is
`Cybenetics`), together with each entry's report date (`source.reportDate`) and Cybenetics ETA grade per input
voltage (`ratingsCybenetics`), are test results provided by [Cybenetics](https://www.cybenetics.com/) and are used
in this project with Cybenetics' written permission.

This data is **not** covered by the MIT licence in `LICENSE`, which applies to the source code and the rest of
the repository. The data stays the property of Cybenetics and is available here only under these conditions:

- Every use shows the credit "Test results provided by Cybenetics" with a link to the original report, which each
  entry carries in `source.url`, and the report date.
- The results are from the single sample Cybenetics tested, at the input voltage given for each curve
  (115 V or 230 V).
- An ETA grade is written "Cybenetics ETA <grade>" together with its input voltage, only where the report gives
  one, without any badge or logo, and is not an 80 Plus rating.
- The data may not be used commercially or resold, and nothing else from the reports (text, graphs, other scores,
  badge images) is included or may be redistributed from here.

If you want to reuse the data outside this project, ask Cybenetics.
