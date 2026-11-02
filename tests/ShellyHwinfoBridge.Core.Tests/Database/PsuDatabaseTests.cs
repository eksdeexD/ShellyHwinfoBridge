using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;

namespace ShellyHwinfoBridge.Core.Tests.Database;

public sealed class PsuDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-tests-" + Guid.NewGuid().ToString("N"));

    public PsuDatabaseTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string DbPath => Path.Combine(_dir, PsuDatabaseLoader.FileName);

    private static string ValidJson => PsuDatabaseJson.Serialize(PsuDatabaseLoader.LoadEmbedded());

    [Fact]
    public void EmbeddedSeedIsValidAndHasTx700WithAttribution()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        Assert.Empty(PsuDatabaseValidator.Validate(db));
        var tx = Assert.Single(db.Psus, p => p.Id == "seasonic-prime-fanless-tx-700");
        Assert.Equal("Cybenetics", tx.Source!.Name);
        Assert.Equal("21PS1872A", tx.Source.ReportId);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/1872/", tx.Source.Url);
        Assert.Equal("2021-07-16", tx.Source.ReportDate);
        Assert.Equal(15, tx.GetCurve(InputVoltage.V230)!.Points.Count);
        Assert.Equal(15, tx.GetCurve(InputVoltage.V115)!.Points.Count);

        Assert.All(db.Psus, p =>
        {
            Assert.Equal("Cybenetics", p.Source!.Name);
            Assert.False(string.IsNullOrEmpty(p.Source.Url));
            Assert.StartsWith("https://www.cybenetics.com/", p.Source.Url, StringComparison.Ordinal);
            // Cybenetics' terms require the report date next to the link: every shipped entry carries one.
            Assert.True(PsuDatabaseValidator.IsIsoDate(p.Source.ReportDate), p.Id + " source.reportDate");
            // Single-voltage reports carry only the curve of the voltage tested (230 V for EU-only units, 115 V for
            // early reports tested at 115 V only); the key of a missing curve is omitted, never null.
            Assert.True(
                (p.GetCurve(InputVoltage.V230)?.Points.Count ?? 0) >= 2 || (p.GetCurve(InputVoltage.V115)?.Points.Count ?? 0) >= 2,
                p.Id + " curve");
            Assert.All(p.Curves!, c => Assert.NotNull(c.Value));
        });
    }

    [Fact]
    public void ValidatorAcceptsASingleVoltageEntryAndRejectsOneWithoutCurves()
    {
        var single = Entry("eu-only", "Brand", "Series", "EU 3200", 3200, "Titanium", null);
        var db = new PsuDatabase { SchemaVersion = 1, Psus = [single] };
        Assert.Empty(PsuDatabaseValidator.Validate(db));
        Assert.Null(single.GetCurve(InputVoltage.V115));
        Assert.NotNull(single.GetCurveSet());

        var json = PsuDatabaseJson.Serialize(db);
        Assert.DoesNotContain("\"115\"", json, StringComparison.Ordinal);
        Assert.Empty(PsuDatabaseValidator.ValidateJson(json, out var roundTripped));
        Assert.Equal(["230"], roundTripped!.Psus[0].Curves!.Keys);

        single.Curves = new() { ["115"] = [[100, 90], [200, 185]] };
        Assert.Empty(PsuDatabaseValidator.Validate(db));

        single.Curves = [];
        Assert.Contains(PsuDatabaseValidator.Validate(db), e => e.Path == "psus[0].curves");
        single.Curves = null;
        Assert.Contains(PsuDatabaseValidator.Validate(db), e => e.Path == "psus[0].curves");

        // A present curve must still pass curve validation, and a null curve is not a way to say "missing".
        single.Curves = new() { ["230"] = [[100, 90], [200, 185]], ["115"] = null! };
        Assert.Contains(PsuDatabaseValidator.Validate(db), e => e.Path == "psus[0].curves.115");
        single.Curves = new() { ["230"] = [[100, 90], [90, 85]] };
        Assert.Contains(PsuDatabaseValidator.Validate(db), e => e.Path == "psus[0].curves.230");
    }

    [Fact]
    public void RepoFileMatchesEmbeddedCopy()
    {
        var repoFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", PsuDatabaseLoader.FileName);
        Assert.Empty(PsuDatabaseValidator.ValidateJson(File.ReadAllText(repoFile), out var db));
        Assert.Equal(ValidJson, PsuDatabaseJson.Serialize(db!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\": 2, \"psus\": []}")]
    public void ValidatorRejectsMalformedJson(string json)
    {
        Assert.NotEmpty(PsuDatabaseValidator.ValidateJson(json, out var db));
        Assert.Null(db);
    }

    [Fact]
    public void ValidatorReportsMissingFieldsAndBadCurves()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        var entry = db.Psus[0];
        entry.Brand = "";
        entry.Source = null;
        entry.Rating80Plus = "Unobtainium";
        entry.Curves!["230"][3] = [80, 90];
        entry.Curves["115"][0] = [1.0];

        var paths = PsuDatabaseValidator.Validate(db).Select(e => e.Path).ToList();
        Assert.Contains("psus[0].brand", paths);
        Assert.Contains("psus[0].source", paths);
        Assert.Contains("psus[0].rating80Plus", paths);
        Assert.Contains("psus[0].curves.230", paths);
        Assert.Contains("psus[0].curves.115[0]", paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatorAcceptsAMissingReportIdWhenTheUrlIsPresent(string? reportId)
    {
        var db = new PsuDatabase { SchemaVersion = 1, Updated = "2026-01-01" };
        var entry = Entry("asus-rog-thor-850p", "ASUS", "ROG Thor", "ROG-THOR-850P", 850, "Platinum", null);
        entry.Source = new PsuSource { Name = "Cybenetics", ReportId = reportId, Url = "https://www.cybenetics.com/evaluations/psus/448/" };
        db.Psus.Add(entry);
        Assert.Empty(PsuDatabaseValidator.Validate(db));

        entry.Source.Url = "";
        Assert.Equal(["psus[0].source.url"], PsuDatabaseValidator.Validate(db).Select(e => e.Path));
    }

    [Fact]
    public void ValidatorRejectsDuplicateIds()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        db.Psus.Add(PsuDatabaseJson.Deserialize(ValidJson).Psus[0]);
        Assert.Contains(PsuDatabaseValidator.Validate(db), e => e.Message.Contains("Duplicate id", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceWithInvalidJsonKeepsExistingFile()
    {
        // SPEC acceptance test 10.
        File.WriteAllText(DbPath, ValidJson);
        var loader = new PsuDatabaseLoader(DbPath);

        var result = loader.TryReplace("{\"schemaVersion\": 1, \"psus\": [ { \"id\": \"broken\" } ]}");

        Assert.False(result.Replaced);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(ValidJson, File.ReadAllText(DbPath));
        Assert.Equal(DatabaseOrigin.Shipped, loader.Load().Origin);
        Assert.False(File.Exists(DbPath + ".tmp"));
    }

    [Fact]
    public void ReplaceWithValidJsonWritesFile()
    {
        File.WriteAllText(DbPath, ValidJson);
        var newDb = PsuDatabaseLoader.LoadEmbedded();
        newDb.Updated = "2027-01-01";
        var newJson = PsuDatabaseJson.Serialize(newDb);

        var result = new PsuDatabaseLoader(DbPath).TryReplace(newJson);

        Assert.True(result.Replaced);
        Assert.Equal(newJson, File.ReadAllText(DbPath));
    }

    [Fact]
    public void LoaderFallsBackToEmbeddedWhenLocalMissingOrInvalid()
    {
        var loader = new PsuDatabaseLoader(DbPath);
        Assert.Equal(DatabaseOrigin.Embedded, loader.Load().Origin);

        File.WriteAllText(DbPath, "garbage");
        var loaded = loader.Load();
        Assert.Equal(DatabaseOrigin.Embedded, loaded.Origin);
        Assert.NotEmpty(loaded.Database.Psus);
        Assert.Contains(loaded.Database.Psus, p => p.Id == "seasonic-prime-fanless-tx-700");
    }

    [Fact]
    public void CascadingFiltersAndSearch()
    {
        // A synthetic database keeps the cascade deterministic however the shipped database grows.
        var db = new PsuDatabase { SchemaVersion = 1, Updated = "2026-01-01" };
        db.Psus.Add(Entry("seasonic-prime-fanless-tx-700", "Seasonic", "Prime Fanless TX", "Prime Fanless TX-700", 700, "Titanium", null));
        db.Psus.Add(Entry("corsair-rm850x-2021", "Corsair", "RMx", "RM850x", 850, "Gold", "2021"));
        db.Psus.Add(Entry("corsair-rm850x-2018", "Corsair", "RMx", "RM850x", 850, "Gold", "2018"));
        db.Psus.Add(Entry("corsair-hx1000i", "Corsair", "HXi", "HX1000i", 1000, "Platinum", null));
        db.Psus.Add(Entry("seasonic-focus-gx-850", "Seasonic", "Focus GX", "Focus GX-850", 850, "Gold", null));
        var q = new PsuDatabaseQuery(db);

        Assert.Equal(["Corsair", "Seasonic"], q.GetBrands());
        Assert.Equal([850, 1000], q.GetWattages("Corsair"));
        Assert.Equal([700, 850], q.GetWattages("seasonic"));
        Assert.Equal(["Gold", "Platinum"], q.GetRatings("Corsair", null));
        Assert.Equal(["Gold"], q.GetRatings("Corsair", 850));
        Assert.Equal(["corsair-rm850x-2018", "corsair-rm850x-2021"], q.GetModels("Corsair", 850, "Gold").Select(e => e.Id));
        Assert.Equal(5, q.GetModels(null, null, null).Count);

        Assert.Equal(["corsair-rm850x-2021"], q.Search("rm850x 2021").Select(e => e.Id));
        Assert.Equal(["seasonic-prime-fanless-tx-700"], q.Search("TX-700").Select(e => e.Id));
        Assert.Empty(q.Search("nothing here"));
        Assert.NotNull(q.FindById("CORSAIR-HX1000I"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("2021-07-16", true)]
    [InlineData("2024-02-29", true)]
    [InlineData("", false)]
    [InlineData("2021-7-16", false)]
    [InlineData("16.07.2021", false)]
    [InlineData("Jul 16, 2021", false)]
    [InlineData("2023-02-29", false)]
    [InlineData(" 2021-07-16", false)]
    public void ValidatorChecksTheReportDateFormatWhenPresent(string? reportDate, bool valid)
    {
        var entry = Entry("a", "Brand", "Series", "A 750", 750, "Gold", null);
        entry.Source!.ReportDate = reportDate;
        var paths = PsuDatabaseValidator.Validate(new PsuDatabase { SchemaVersion = 1, Psus = [entry] }).Select(e => e.Path).ToList();
        Assert.Equal(valid ? [] : ["psus[0].source.reportDate"], paths);
    }

    [Fact]
    public void ReportDateIsWrittenAfterTheUrlAndLeftOutWhenNull()
    {
        var entry = Entry("a", "Brand", "Series", "A 750", 750, "Gold", null);
        var db = new PsuDatabase { SchemaVersion = 1, Psus = [entry] };
        Assert.DoesNotContain("reportDate", PsuDatabaseJson.Serialize(db), StringComparison.Ordinal);

        entry.Source!.ReportDate = "2021-07-16";
        var json = PsuDatabaseJson.Serialize(db);
        var url = json.IndexOf("\"url\"", StringComparison.Ordinal);
        var date = json.IndexOf("\"reportDate\": \"2021-07-16\"", StringComparison.Ordinal);
        Assert.True(url >= 0 && date > url, json);
        Assert.Equal("2021-07-16", PsuDatabaseJson.Deserialize(json).Psus[0].Source!.ReportDate);
    }

    [Fact]
    public void ADatabaseWithoutReportDatesStillLoads()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "updated": "2026-10-31",
              "psus": [
                {
                  "id": "a", "brand": "Brand", "series": "Series", "model": "A 750", "revision": null, "wattage": 750,
                  "rating80Plus": "Gold", "ratingCybenetics": null,
                  "source": { "name": "Cybenetics", "reportId": "21PS1872A", "url": "https://www.cybenetics.com/evaluations/psus/1872/" },
                  "curves": { "230": [[100, 90], [200, 185]] }
                }
              ]
            }
            """;
        Assert.Empty(PsuDatabaseValidator.ValidateJson(json, out var db));
        Assert.Null(db!.Psus[0].Source!.ReportDate);
    }

    [Fact]
    public void A100ClientIgnoresTheReportDateField()
    {
        // 1.0.0 deserialises with these options into a PsuSource without ReportDate; System.Text.Json skips unknown
        // members by default, so a database with reportDate must still load there. V100Source mirrors that shape.
        Assert.Equal(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip, PsuDatabaseJson.Options.UnmappedMemberHandling);
        const string json = """
            { "name": "Cybenetics", "reportId": "21PS1872A", "url": "https://www.cybenetics.com/evaluations/psus/1872/", "reportDate": "2021-07-16" }
            """;
        var source = System.Text.Json.JsonSerializer.Deserialize<V100Source>(json, PsuDatabaseJson.Options)!;
        Assert.Equal("21PS1872A", source.ReportId);
        Assert.Equal("https://www.cybenetics.com/evaluations/psus/1872/", source.Url);

        // And the whole shipped database, with its dates, loads into the 1.0.0 model.
        var repoFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", PsuDatabaseLoader.FileName);
        var old = System.Text.Json.JsonSerializer.Deserialize<V100Database>(File.ReadAllText(repoFile), PsuDatabaseJson.Options)!;
        Assert.Equal(PsuDatabaseLoader.LoadEmbedded().Psus.Count, old.Psus.Count);
        Assert.All(old.Psus, p => Assert.False(string.IsNullOrEmpty(p.Source!.Url)));
    }

    private sealed class V100Database
    {
        public int SchemaVersion { get; set; }

        public List<V100Entry> Psus { get; set; } = [];
    }

    [Fact]
    public void A100ClientLoadsTheShippedDatabaseWithPerVoltageEtaGrades()
    {
        // 1.0.0 clients download psu-database.json from main. Their PsuEntry has a single string RatingCybenetics and no
        // ratingsCybenetics; their validator never looks at the rating (git show v1.0.0's PsuDatabaseValidator). The
        // object-valued ratingsCybenetics is skipped as an unknown member and the missing ratingCybenetics stays null,
        // so the file loads there and those clients simply show no ETA grade. V100Entry mirrors the 1.0.0 shape.
        var text = File.ReadAllText(RepoFile);
        Assert.Contains("\"ratingsCybenetics\": {", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ratingCybenetics\"", text, StringComparison.Ordinal);

        var old = System.Text.Json.JsonSerializer.Deserialize<V100Database>(text, PsuDatabaseJson.Options)!;
        Assert.Equal(1, old.SchemaVersion);
        Assert.Equal(PsuDatabaseLoader.LoadEmbedded().Psus.Count, old.Psus.Count);
        Assert.All(old.Psus, p =>
        {
            Assert.Null(p.RatingCybenetics);
            Assert.False(string.IsNullOrEmpty(p.Rating80Plus));
            Assert.True(p.Wattage > 0);
            Assert.NotEmpty(p.Curves!);
        });
    }

    [Fact]
    public void ShippedDatabaseCarriesPerVoltageEtaGradesOnlyWithKnownValues()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        var tx = db.Psus.Single(p => p.Id == "seasonic-prime-fanless-tx-700");
        Assert.Equal("Titanium", tx.GetEtaRating(InputVoltage.V230));
        Assert.Equal("Titanium", tx.GetEtaRating(InputVoltage.V115));
        Assert.All(db.Psus.Where(p => p.RatingsCybenetics is not null), p =>
        {
            Assert.NotEmpty(p.RatingsCybenetics!);
            Assert.All(p.RatingsCybenetics!, g => Assert.Contains(g.Value, PsuDatabaseValidator.AllowedEtaRatings));
        });
    }

    [Theory]
    [InlineData("""{ "230": "Gold", "115": "Platinum" }""", new string[0])]
    [InlineData("""{ "230": "gold" }""", new string[0])]
    [InlineData("""{ "230": "Diamond", "115": "Standard" }""", new string[0])]
    [InlineData("""{ "240": "Gold" }""", new[] { "psus[0].ratingsCybenetics.240" })]
    [InlineData("""{ "115": "White" }""", new[] { "psus[0].ratingsCybenetics.115" })]
    [InlineData("""{ "230": "None" }""", new[] { "psus[0].ratingsCybenetics.230" })]
    public void ValidatorChecksTheEtaKeysAndGrades(string ratings, string[] errorPaths)
    {
        var json = $$"""
            { "schemaVersion": 1, "psus": [ {
              "id": "a", "brand": "Brand", "series": "Series", "model": "A 750", "wattage": 750, "rating80Plus": "None",
              "ratingsCybenetics": {{ratings}},
              "source": { "name": "Cybenetics", "url": "https://www.cybenetics.com/evaluations/psus/1/" },
              "curves": { "230": [[100, 90], [200, 185]] } } ] }
            """;
        Assert.Equal(errorPaths, PsuDatabaseValidator.ValidateJson(json, out _).Select(e => e.Path));
    }

    [Fact]
    public void TheOldSingleEtaGradeIsReadAs230VAndNeverWritten()
    {
        const string json = """
            { "schemaVersion": 1, "psus": [ {
              "id": "a", "brand": "Brand", "series": "Series", "model": "A 750", "wattage": 750, "rating80Plus": "None",
              "ratingCybenetics": "Gold",
              "source": { "name": "Cybenetics", "url": "https://www.cybenetics.com/evaluations/psus/1/" },
              "curves": { "230": [[100, 90], [200, 185]], "115": [[100, 88], [200, 182]] } } ] }
            """;
        Assert.Empty(PsuDatabaseValidator.ValidateJson(json, out var db));
        var entry = db!.Psus[0];
        Assert.Equal("Gold", entry.GetEtaRating(InputVoltage.V230));
        Assert.Null(entry.GetEtaRating(InputVoltage.V115));

        var written = PsuDatabaseJson.Serialize(db);
        Assert.DoesNotContain("\"ratingCybenetics\"", written, StringComparison.Ordinal);
        Assert.Contains("\"ratingsCybenetics\"", written, StringComparison.Ordinal);
        Assert.Equal("Gold", PsuDatabaseJson.Deserialize(written).Psus[0].GetEtaRating(InputVoltage.V230));

        // When both are present, the per-voltage grades win, in either order.
        foreach (var both in new[]
        {
            """{ "ratingCybenetics": "Gold", "ratingsCybenetics": { "230": "Platinum" } }""",
            """{ "ratingsCybenetics": { "230": "Platinum" }, "ratingCybenetics": "Gold" }""",
        })
        {
            var e = System.Text.Json.JsonSerializer.Deserialize<PsuEntry>(both, PsuDatabaseJson.Options)!;
            Assert.Equal("Platinum", e.GetEtaRating(InputVoltage.V230));
        }

        // No grades: the member is left out.
        Assert.DoesNotContain("atingsCybenetics", PsuDatabaseJson.Serialize(new PsuDatabase { SchemaVersion = 1, Psus = [Entry("b", "B", "S", "M", 650, "Gold", null)] }), StringComparison.Ordinal);
    }

    private static string RepoFile => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", PsuDatabaseLoader.FileName);

    private sealed class V100Entry
    {
        public string Id { get; set; } = string.Empty;

        public int Wattage { get; set; }

        public string Rating80Plus { get; set; } = string.Empty;

        public string? RatingCybenetics { get; set; }

        public V100Source? Source { get; set; }

        public Dictionary<string, List<double[]>>? Curves { get; set; }
    }

    private sealed class V100Source
    {
        public string Name { get; set; } = string.Empty;

        public string? ReportId { get; set; }

        public string Url { get; set; } = string.Empty;
    }

    [Theory]
    [InlineData("RM1000e", "ATX v3.1", "RM1000e (ATX v3.1)")]
    [InlineData("Prime TX-700", "2021", "Prime TX-700 (2021)")]
    [InlineData("MWE Gold 1050 V2", "ATX v3.0 (2024)", "MWE Gold 1050 V2 (ATX v3.0, 2024)")]
    [InlineData("Toughpower GF A3", "2018 (#2)", "Toughpower GF A3 (2018, #2)")]
    [InlineData("V Platinum 1100 V2", null, "V Platinum 1100 V2")]
    [InlineData("V Platinum 1100 V2", "  ", "V Platinum 1100 V2")]
    [InlineData("V Platinum 1100 V2", "v2", "V Platinum 1100 V2")]
    [InlineData("Focus GX-750 ATX v3.1", "atx V3.1", "Focus GX-750 ATX v3.1")]
    public void DisplayNameAppendsTheRevisionUnlessTheModelAlreadyHasIt(string model, string? revision, string expected)
    {
        var entry = Entry("x", "Corsair", "S", model, 700, "Gold", revision);
        Assert.Equal(expected, entry.DisplayName);
        Assert.Equal("Corsair " + expected, entry.FullName);
    }

    [Fact]
    public void DisplayNamesAreNotWrittenToTheJson()
    {
        var db = new PsuDatabase { SchemaVersion = 1, Psus = [Entry("x", "Corsair", "RM", "RM1000e", 1000, "Gold", "ATX v3.1")] };
        var json = PsuDatabaseJson.Serialize(db);
        Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchMatchesTheRevisionAlsoAsShownInTheList()
    {
        var query = new PsuDatabaseQuery(new PsuDatabase
        {
            SchemaVersion = 1,
            Psus =
            [
                Entry("old", "Corsair", "RMe", "RM1000e", 1000, "Gold", null),
                Entry("new", "Corsair", "RMe", "RM1000e", 1000, "Gold", "ATX v3.1"),
                Entry("mwe", "Cooler Master", "MWE Gold", "MWE Gold 1050 V2", 1050, "Gold", "ATX v3.0 (2024)"),
            ],
        });

        Assert.Equal(["new"], query.Search("rm1000e v3.1").Select(e => e.Id));
        Assert.Equal(["new"], query.Search("Corsair RM1000e (ATX v3.1)").Select(e => e.Id));
        Assert.Equal(["mwe"], query.Search("MWE Gold 1050 V2 (ATX v3.0, 2024)").Select(e => e.Id));
        Assert.Equal(["mwe"], query.Search("(2024)").Select(e => e.Id));
        Assert.Equal(3, query.Search(" ( ) , ").Count);
    }

    private static PsuEntry Entry(string id, string brand, string series, string model, int watts, string rating, string? revision) => new()
    {
        Id = id,
        Brand = brand,
        Series = series,
        Model = model,
        Revision = revision,
        Wattage = watts,
        Rating80Plus = rating,
        Source = new PsuSource { Name = "Test", Url = "https://example.com/" },
        Curves = new() { ["230"] = [[100, 90], [200, 185]] },
    };
}
