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
        Assert.Equal(15, tx.GetCurve(InputVoltage.V230)!.Points.Count);
        Assert.Equal(15, tx.GetCurve(InputVoltage.V115)!.Points.Count);

        Assert.All(db.Psus, p =>
        {
            Assert.Equal("Cybenetics", p.Source!.Name);
            Assert.False(string.IsNullOrEmpty(p.Source.Url));
            Assert.StartsWith("https://www.cybenetics.com/", p.Source.Url, StringComparison.Ordinal);
            Assert.True(p.GetCurve(InputVoltage.V230)!.Points.Count >= 2, p.Id + " 230 V curve");
            Assert.True(p.GetCurve(InputVoltage.V115)!.Points.Count >= 2, p.Id + " 115 V curve");
        });
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
        Assert.Equal(DatabaseOrigin.LocalFile, loader.Load().Origin);
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
