using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Database;

/// <summary>SPEC "Database updates under Program Files": downloaded copy vs shipped copy vs embedded copy.</summary>
public sealed class PsuDatabaseLoaderPreferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shb-dbpref-" + Guid.NewGuid().ToString("N"));

    public PsuDatabaseLoaderPreferenceTests()
    {
        // Two separate folders: the read-only install folder and %APPDATA%\ShellyHwinfoBridge.
        Directory.CreateDirectory(Path.Combine(_root, "Program Files"));
    }

    private string ShippedPath => Path.Combine(_root, "Program Files", PsuDatabaseLoader.FileName);

    private string DownloadedPath => Path.Combine(_root, "AppData", PsuDatabaseLoader.FileName);

    private PsuDatabaseLoader Loader => new(ShippedPath, DownloadedPath);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The embedded database with another date and, optionally, extra copies of the first entry.</summary>
    private static string Json(string? updated, int extraEntries = 0)
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        db.Updated = updated;
        var template = PsuDatabaseJson.Serialize(db);
        for (var i = 0; i < extraEntries; i++)
        {
            var copy = PsuDatabaseJson.Deserialize(template).Psus[0];
            copy.Id = "extra-entry-" + i;
            copy.Model = "Extra " + i;
            db.Psus.Add(copy);
        }

        return PsuDatabaseJson.Serialize(db);
    }

    private void WriteShipped(string json) => File.WriteAllText(ShippedPath, json);

    private void WriteDownloaded(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DownloadedPath)!);
        File.WriteAllText(DownloadedPath, json);
    }

    [Fact]
    public void WithoutADownloadTheShippedCopyIsUsed()
    {
        WriteShipped(Json("2026-10-31"));

        var loaded = Loader.Load();

        Assert.Equal(DatabaseOrigin.Shipped, loaded.Origin);
        Assert.Equal("2026-10-31", loaded.Database.Updated);
    }

    [Fact]
    public void ANewerDownloadedCopyWins()
    {
        WriteShipped(Json("2026-10-31"));
        WriteDownloaded(Json("2026-12-01"));

        var loaded = Loader.Load();

        Assert.Equal(DatabaseOrigin.Downloaded, loaded.Origin);
        Assert.Equal("2026-12-01", loaded.Database.Updated);
    }

    [Fact]
    public void AnOlderDownloadedCopyLosesToANewerShippedCopy()
    {
        // The installer upgraded the app (and its database) after an earlier download.
        WriteShipped(Json("2027-02-01"));
        WriteDownloaded(Json("2026-12-01", extraEntries: 5));

        var loaded = Loader.Load();

        Assert.Equal(DatabaseOrigin.Shipped, loaded.Origin);
        Assert.Equal("2027-02-01", loaded.Database.Updated);
    }

    [Fact]
    public void WithTheSameDateTheEntryCountDecides()
    {
        WriteShipped(Json("2026-10-31"));
        WriteDownloaded(Json("2026-10-31"));
        Assert.Equal(DatabaseOrigin.Shipped, Loader.Load().Origin);

        WriteDownloaded(Json("2026-10-31", extraEntries: 2));
        Assert.Equal(DatabaseOrigin.Downloaded, Loader.Load().Origin);
    }

    [Fact]
    public void WithoutADateTheEntryCountDecides()
    {
        WriteShipped(Json("2026-10-31", extraEntries: 1));
        WriteDownloaded(Json(null));
        Assert.Equal(DatabaseOrigin.Shipped, Loader.Load().Origin);

        WriteDownloaded(Json(null, extraEntries: 3));
        Assert.Equal(DatabaseOrigin.Downloaded, Loader.Load().Origin);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("{\"schemaVersion\": 1, \"updated\": \"2099-01-01\", \"psus\": [ { \"id\": \"broken\" } ]}")]
    [InlineData("")]
    public void ACorruptDownloadedCopyFallsBackToTheShippedCopy(string corrupt)
    {
        WriteShipped(Json("2026-10-31"));
        WriteDownloaded(corrupt);

        var loaded = Loader.Load();

        Assert.Equal(DatabaseOrigin.Shipped, loaded.Origin);
        Assert.Equal("2026-10-31", loaded.Database.Updated);
    }

    [Fact]
    public void ACorruptDownloadedCopyAndAMissingShippedCopyFallBackToTheEmbeddedCopy()
    {
        WriteDownloaded("{ not json");

        var loaded = Loader.Load();

        Assert.Equal(DatabaseOrigin.Embedded, loaded.Origin);
        Assert.Contains(loaded.Database.Psus, p => p.Id == "seasonic-prime-fanless-tx-700");
    }

    [Fact]
    public void ANewerDownloadedCopyWinsOverTheEmbeddedCopyWhenTheShippedCopyIsMissingOrInvalid()
    {
        WriteShipped("garbage");
        WriteDownloaded(Json("2099-01-01"));

        Assert.Equal(DatabaseOrigin.Downloaded, Loader.Load().Origin);

        WriteDownloaded(Json(PsuDatabaseLoader.LoadEmbedded().Updated));
        Assert.Equal(DatabaseOrigin.Embedded, Loader.Load().Origin);
    }

    [Fact]
    public void TryReplaceWritesTheDownloadedCopyAndLeavesTheShippedCopyAlone()
    {
        var shipped = Json("2026-10-31");
        WriteShipped(shipped);
        var loader = Loader;

        var result = loader.TryReplace(Json("2026-12-01"));

        Assert.True(result.Replaced);
        Assert.Equal(DatabaseOrigin.Downloaded, loader.WriteOrigin);
        Assert.Equal(shipped, File.ReadAllText(ShippedPath));
        Assert.True(File.Exists(DownloadedPath));
        Assert.Equal(DatabaseOrigin.Downloaded, loader.Load().Origin);
    }

    [Fact]
    public async Task CheckForUpdateDownloadsIntoTheAppDataFolder()
    {
        var shipped = Json("2026-10-31");
        WriteShipped(shipped);
        var newer = Json("2026-12-01", extraEntries: 1);
        using var updater = new DatabaseUpdater(Loader, new StubHttpHandler().Add("/db.json", newer));

        var result = await updater.CheckForUpdateAsync("https://example.test/db.json");

        Assert.Equal(DatabaseUpdateStatus.Updated, result.Status);
        Assert.Equal(newer, File.ReadAllText(DownloadedPath));
        Assert.Equal(shipped, File.ReadAllText(ShippedPath));

        // A second check finds the downloaded copy current.
        using var again = new DatabaseUpdater(Loader, new StubHttpHandler().Add("/db.json", newer));
        Assert.Equal(DatabaseUpdateStatus.AlreadyCurrent, (await again.CheckForUpdateAsync("https://example.test/db.json")).Status);
    }

    [Fact]
    public void TheDefaultDownloadedPathIsInTheAppDataFolder()
    {
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, PsuDatabaseLoader.FileName), PsuDatabaseLoader.DefaultDownloadedPath);
        Assert.Equal(AppPaths.DownloadedDatabaseFile, PsuDatabaseLoader.DefaultDownloadedPath);
        Assert.Null(new PsuDatabaseLoader(ShippedPath).DownloadedPath);
        Assert.Equal(ShippedPath, new PsuDatabaseLoader(ShippedPath).WritePath);
    }
}
