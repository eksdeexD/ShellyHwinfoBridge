using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Logging;
using ShellyHwinfoBridge.Core.Polling;
using ShellyHwinfoBridge.Core.Sensors;
using ShellyHwinfoBridge.Core.Settings;

namespace ShellyHwinfoBridge.Core.Tests.Settings;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shb-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileLoadsAsNull()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"), new ReversingProtector());
        Assert.False(store.Exists);
        Assert.Null(store.Load());
    }

    [Fact]
    public void RoundTripsAllSettingsWithProtectedPassword()
    {
        var store = new SettingsStore(Path.Combine(_dir, "sub", "settings.json"), new ReversingProtector());
        var settings = new AppSettings
        {
            Device = { Host = "192.168.1.50", Name = "Desk plug", Model = "S3PL-00112EU" },
            Psu = { Mode = PsuSelectionMode.Manual, ManualCurve = [new CurvePoint(100, 80), new CurvePoint(200, 180)] },
            VoltageMode = InputVoltageMode.V115,
            SmoothingSamples = 5,
            DisplayIntervalMs = 3500,
            HttpTimeoutMs = 700,
            Sensors = [new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu, Device = "GPU [#0]" }],
            HideTrayIcon = true,
            StartWithWindows = true,
        };
        store.SetPassword(settings, "s3cret");
        store.Save(settings);

        var json = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("s3cret", json, StringComparison.Ordinal);
        Assert.Contains("\"Manual\"", json, StringComparison.Ordinal);

        var loaded = store.Load()!;
        Assert.Equal("s3cret", store.GetPassword(loaded));
        Assert.Equal("192.168.1.50", loaded.Device.Host);
        Assert.Equal(InputVoltageMode.V115, loaded.VoltageMode);
        Assert.Equal(5, loaded.SmoothingSamples);
        Assert.Equal((3500, 700), (loaded.DisplayIntervalMs, loaded.HttpTimeoutMs));
        Assert.Null(loaded.PollIntervalMs);
        Assert.DoesNotContain("pollIntervalMs", json, StringComparison.Ordinal);
        Assert.Equal(settings.Psu.ManualCurve, loaded.Psu.ManualCurve);
        Assert.Equal(SensorCategory.Gpu, Assert.Single(loaded.Sensors).Category);
        Assert.True(loaded.HideTrayIcon);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal("GPU Power", loaded.Sensors[0].Name);
    }

    [Fact]
    public void ClearingPasswordRemovesIt()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"), new ReversingProtector());
        var settings = new AppSettings();
        store.SetPassword(settings, "x");
        store.SetPassword(settings, "");
        Assert.Null(settings.Device.ProtectedPassword);
        Assert.Null(store.GetPassword(settings));
    }

    [Fact]
    public void LoadNormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"pollIntervalMs": 50, "httpTimeoutMs": 5000, "smoothingSamples": 0}""");

        var loaded = new SettingsStore(path, new ReversingProtector()).Load()!;

        Assert.Equal((1000, 900, 1), (loaded.DisplayIntervalMs, loaded.HttpTimeoutMs, loaded.SmoothingSamples));
        Assert.Null(loaded.PollIntervalMs);
    }

    [Fact]
    public void OldSettingsFileWithoutEnergyFieldsEnablesEnergyLogging()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"schemaVersion": 1, "device": {"host": "192.168.1.50"}, "pollIntervalMs": 1000}""");

        var loaded = new SettingsStore(path, new ReversingProtector()).Load()!;

        Assert.True(loaded.EnergyLoggingEnabled);
        Assert.Null(loaded.EnergyPricePerKwh);
        Assert.Null(loaded.EnergyCurrency);
    }

    [Fact]
    public void EnergySettingsRoundTripAndNormalize()
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"), new ReversingProtector());
        store.Save(new AppSettings { EnergyLoggingEnabled = false, EnergyPricePerKwh = 0.32m, EnergyCurrency = " DKK " });

        var loaded = store.Load()!;
        Assert.False(loaded.EnergyLoggingEnabled);
        Assert.Equal(0.32m, loaded.EnergyPricePerKwh);
        Assert.Equal("DKK", loaded.EnergyCurrency);

        var odd = new AppSettings { EnergyPricePerKwh = -1m, EnergyCurrency = "VeryLongCurrency" };
        odd.Normalize();
        Assert.Equal(0m, odd.EnergyPricePerKwh);
        Assert.Equal("VeryLong", odd.EnergyCurrency);

        var blank = new AppSettings { EnergyPricePerKwh = 5000m, EnergyCurrency = "  " };
        blank.Normalize();
        Assert.Equal(AppSettings.MaxPricePerKwh, blank.EnergyPricePerKwh);
        Assert.Null(blank.EnergyCurrency);
    }

    [Fact]
    public void CloneIsADeepCopy()
    {
        var settings = new AppSettings
        {
            Device = { Host = "192.168.1.50", ProtectedPassword = "enc:x" },
            Psu = { Mode = PsuSelectionMode.Manual, ManualCurve = [new CurvePoint(100, 80)] },
            Sensors = [new SensorSelection { Name = "GPU Power", Category = SensorCategory.Gpu }],
            EnergyPricePerKwh = 0.32m,
            VoltageMode = InputVoltageMode.V115,
        };

        var clone = settings.Clone();
        clone.Device.Host = "10.0.0.9";
        clone.Sensors.Add(new SensorSelection { Name = "CPU Package Power" });
        clone.Psu.ManualCurve!.Add(new CurvePoint(200, 180));

        Assert.Equal("192.168.1.50", settings.Device.Host);
        Assert.Single(settings.Sensors);
        Assert.Single(settings.Psu.ManualCurve!);
        Assert.Equal("enc:x", clone.Device.ProtectedPassword);
        Assert.Equal(0.32m, clone.EnergyPricePerKwh);
        Assert.Equal(InputVoltageMode.V115, clone.VoltageMode);
    }

    [Fact]
    public void CorruptFileThrowsInvalidData()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ nope");
        Assert.Throws<InvalidDataException>(() => new SettingsStore(path, new ReversingProtector()).Load());
    }

    [Fact]
    public void RetiredCustomFormulaRoundTripsButIsNotApplied()
    {
        var settings = new AppSettings { Sensors = [new SensorSelection { Name = "GPU Power" }], CustomFormula = "\"DC Power (Est.)\" - 50" };
        Assert.Equal("\"DC Power (Est.)\" - 50", settings.Clone().CustomFormula);
        Assert.Equal("PC Power (DC) − GPU Power", ShellyHwinfoBridge.Core.Presentation.SettingsPresenter.CalculationText(settings));
    }

    [Fact]
    public void ResolvesCurvesForEachPsuMode()
    {
        var db = PsuDatabaseLoader.LoadEmbedded();
        Assert.NotNull(PsuCurveResolver.Resolve(new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "seasonic-prime-fanless-tx-700" }, db)?.Curve115);
        Assert.Null(PsuCurveResolver.Resolve(new PsuSettings { Mode = PsuSelectionMode.Database, DatabaseId = "nope" }, db));
        Assert.NotNull(PsuCurveResolver.Resolve(new PsuSettings { Mode = PsuSelectionMode.Generic, GenericWattage = 850, GenericRating = EightyPlusRating.Gold }, db));
        Assert.Null(PsuCurveResolver.Resolve(new PsuSettings { Mode = PsuSelectionMode.Generic }, db));
        Assert.Null(PsuCurveResolver.Resolve(new PsuSettings { Mode = PsuSelectionMode.Manual, ManualCurve = [new CurvePoint(100, 120)] }, db));
    }

    [Fact]
    public void RollingLoggerKeepsAtMostThreeFiles()
    {
        using var provider = new RollingFileLoggerProvider(_dir, "test", maxFileBytes: 2048, maxFiles: 3);
        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 200; i++)
        {
            logger.LogInformation("Message number {Number} with some padding text to fill the file.", i);
        }

        logger.LogDebug("Below minimum level");

        var files = Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["test.1.log", "test.2.log", "test.log"], files);
        Assert.All(Directory.GetFiles(_dir), f => Assert.True(new FileInfo(f).Length <= 2048));
        Assert.Contains("Message number 199", File.ReadAllText(provider.CurrentFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("Below minimum level", File.ReadAllText(provider.CurrentFilePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"pollIntervalMs": 2000}""", 2000)]
    [InlineData("""{"pollIntervalMs": 500}""", 1000)]
    [InlineData("""{"pollIntervalMs": 9000}""", 5000)]
    [InlineData("""{"pollIntervalMs": 1000}""", 1000)]
    [InlineData("""{"pollIntervalMs": 4000, "displayIntervalMs": 1500}""", 1500)]
    [InlineData("""{"displayIntervalMs": 9000}""", 5000)]
    [InlineData("""{"displayIntervalMs": 3200}""", 3000)]
    [InlineData("""{}""", AppSettings.DefaultDisplayIntervalMs)]
    public void PollIntervalMigratesIntoDisplayInterval(string json, int expected)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);
        var store = new SettingsStore(path, new ReversingProtector());

        var loaded = store.Load()!;

        Assert.Equal(expected, loaded.DisplayIntervalMs);
        Assert.Null(loaded.PollIntervalMs);
        Assert.Equal(PollingOptions.DefaultIntervalMs, loaded.GetPollingOptions().IntervalMs);

        // Saved again: the retired key is gone and the display interval stays.
        store.Save(loaded);
        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("pollIntervalMs", saved, StringComparison.Ordinal);
        Assert.Contains("\"displayIntervalMs\": " + expected, saved, StringComparison.Ordinal);
        Assert.Equal(expected, store.Load()!.DisplayIntervalMs);
    }

    [Fact]
    public void PollingIsFixedAtTheDefaultInterval()
    {
        var settings = new AppSettings { HttpTimeoutMs = 5000 };
        settings.Normalize();

        var options = settings.GetPollingOptions();

        Assert.Equal(PollingOptions.DefaultIntervalMs, options.IntervalMs);
        Assert.Equal(PollingOptions.DefaultIntervalMs - PollingOptions.TimeoutMarginMs, options.TimeoutMs);
        Assert.Equal(AppSettings.DefaultDisplayIntervalMs, new AppSettings().DisplayIntervalMs);
    }

    /// <summary>Test stand-in for DPAPI: reverses and prefixes the text.</summary>
    private sealed class ReversingProtector : IPasswordProtector
    {
        public string Protect(string plainText) => "enc:" + new string(plainText.Reverse().ToArray());

        public string Unprotect(string protectedText) => new(protectedText["enc:".Length..].Reverse().ToArray());
    }
}
