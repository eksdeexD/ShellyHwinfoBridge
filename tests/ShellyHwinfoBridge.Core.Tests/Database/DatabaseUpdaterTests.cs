using System.Net;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Tests.TestSupport;

namespace ShellyHwinfoBridge.Core.Tests.Database;

public sealed class DatabaseUpdaterTests : IDisposable
{
    private const string Url = "https://example.test/db/psu-database.json";
    private const string UrlPath = "/db/psu-database.json";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-tests-" + Guid.NewGuid().ToString("N"));

    public DatabaseUpdaterTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(DbPath, CurrentJson);
    }

    private static string CurrentJson => JsonWithDate(PsuDatabaseLoader.LoadEmbedded().Updated!);

    private string DbPath => Path.Combine(_dir, PsuDatabaseLoader.FileName);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ValidNewerDatabaseReplacesLocalFile()
    {
        var newer = JsonWithDate("2099-01-01");
        using var updater = Updater(new StubHttpHandler().Add(UrlPath, newer));

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.Updated, result.Status);
        Assert.Equal("2099-01-01", result.Database!.Updated);
        Assert.Equal(newer, File.ReadAllText(DbPath));
    }

    [Fact]
    public async Task SameOrOlderDateIsAlreadyCurrent()
    {
        using var updater = Updater(new StubHttpHandler().Add(UrlPath, JsonWithDate("2000-01-01")));

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.AlreadyCurrent, result.Status);
        Assert.Equal(CurrentJson, File.ReadAllText(DbPath));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("{\"schemaVersion\": 1, \"updated\": \"2099-01-01\", \"psus\": [ { \"id\": \"broken\" } ]}")]
    public async Task InvalidJsonKeepsCurrentFile(string body)
    {
        // SPEC acceptance test 10.
        using var updater = Updater(new StubHttpHandler().Add(UrlPath, body));

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(CurrentJson, File.ReadAllText(DbPath));
    }

    [Fact]
    public async Task MissingUpdatedDateIsInvalid()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        db.Updated = null;
        using var updater = Updater(new StubHttpHandler().Add(UrlPath, PsuDatabaseJson.Serialize(db)));

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.Invalid, result.Status);
        Assert.Equal(CurrentJson, File.ReadAllText(DbPath));
    }

    [Fact]
    public async Task Http404IsNetworkError()
    {
        using var updater = Updater(new StubHttpHandler());

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.NetworkError, result.Status);
        Assert.Contains("404", result.Message, StringComparison.Ordinal);
        Assert.Equal(CurrentJson, File.ReadAllText(DbPath));
    }

    [Fact]
    public async Task ConnectionFailureIsNetworkError()
    {
        using var updater = Updater(new ThrowingHandler());

        var result = await updater.CheckForUpdateAsync(Url);

        Assert.Equal(DatabaseUpdateStatus.NetworkError, result.Status);
    }

    [Fact]
    public async Task UsesEmbeddedDatabaseAsCurrentWhenNoLocalFile()
    {
        File.Delete(DbPath);
        using var updater = Updater(new StubHttpHandler().Add(UrlPath, CurrentJson));

        Assert.Equal(DatabaseUpdateStatus.AlreadyCurrent, (await updater.CheckForUpdateAsync(Url)).Status);
        Assert.False(File.Exists(DbPath));
    }

    [Fact]
    public void DefaultUpdateUrlIsTheProjectRawUrl()
    {
        var settings = new AppSettings();
        Assert.Equal("https://raw.githubusercontent.com/eksdeexD/ShellyHwinfoBridge/main/psu-database.json", settings.GetEffectiveDatabaseUpdateUrl());
        settings.DatabaseUpdateUrl = " https://example.test/x.json ";
        Assert.Equal("https://example.test/x.json", settings.GetEffectiveDatabaseUpdateUrl());
    }

    private static string JsonWithDate(string updated)
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        db.Updated = updated;
        return PsuDatabaseJson.Serialize(db);
    }

    private DatabaseUpdater Updater(HttpMessageHandler handler) => new(new PsuDatabaseLoader(DbPath), handler);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.", null, HttpStatusCode.ServiceUnavailable);
    }
}
