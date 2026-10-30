using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core.Curves;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Logging;
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
            PollIntervalMs = 2000,
            HttpTimeoutMs = 1500,
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
        Assert.Equal((2000, 1500), (loaded.PollIntervalMs, loaded.HttpTimeoutMs));
        Assert.Equal(settings.Psu.ManualCurve, loaded.Psu.ManualCurve);
        Assert.Equal(SensorCategory.Gpu, Assert.Single(loaded.Sensors).Category);
        Assert.True(loaded.HideTrayIcon);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal("\"DC Power (Est.)\" - \"GPU Power\"", loaded.GetEffectiveFormula());
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

        Assert.Equal((500, 400, 1), (loaded.PollIntervalMs, loaded.HttpTimeoutMs, loaded.SmoothingSamples));
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
    public void CustomFormulaOverridesGenerated()
    {
        var settings = new AppSettings { Sensors = [new SensorSelection { Name = "GPU Power" }], CustomFormula = "\"DC Power (Est.)\" - 50" };
        Assert.Equal("\"DC Power (Est.)\" - 50", settings.GetEffectiveFormula());
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

    /// <summary>Test stand-in for DPAPI: reverses and prefixes the text.</summary>
    private sealed class ReversingProtector : IPasswordProtector
    {
        public string Protect(string plainText) => "enc:" + new string(plainText.Reverse().ToArray());

        public string Unprotect(string protectedText) => new(protectedText["enc:".Length..].Reverse().ToArray());
    }
}
