using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.App;
using ShellyHwinfoBridge.Core.Database;
using ShellyHwinfoBridge.Core.Energy;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Hwinfo.Locator;
using ShellyHwinfoBridge.Core.Hwinfo.SharedMemory;
using ShellyHwinfoBridge.Core.Settings;
using ShellyHwinfoBridge.Core.Shelly.Discovery;
using ShellyHwinfoBridge.Core.Startup;

namespace ShellyHwinfoBridge;

/// <summary>The composition root: every long-lived service of the app, created once in <see cref="Create"/>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class AppServices : IDisposable
{
    private AppServices(
        ILoggerFactory loggerFactory,
        SettingsStore store,
        PsuDatabaseLoader databaseLoader,
        DatabaseUpdater databaseUpdater,
        HwinfoSensorRegistry registry,
        AutostartManager autostart,
        HwinfoLocator locator,
        HwinfoSharedMemoryReader sharedMemory,
        ShellyDiscovery discovery,
        EnergyLogger energy,
        BridgeRuntime runtime,
        HwinfoMonitor monitor,
        bool settingsWereInvalid)
    {
        LoggerFactory = loggerFactory;
        Store = store;
        DatabaseLoader = databaseLoader;
        DatabaseUpdater = databaseUpdater;
        Registry = registry;
        Autostart = autostart;
        Locator = locator;
        SharedMemory = sharedMemory;
        Discovery = discovery;
        Energy = energy;
        Runtime = runtime;
        Monitor = monitor;
        SettingsWereInvalid = settingsWereInvalid;
    }

    public ILoggerFactory LoggerFactory { get; }

    public SettingsStore Store { get; }

    public PsuDatabaseLoader DatabaseLoader { get; }

    public DatabaseUpdater DatabaseUpdater { get; }

    public HwinfoSensorRegistry Registry { get; }

    public AutostartManager Autostart { get; }

    public HwinfoLocator Locator { get; }

    public HwinfoSharedMemoryReader SharedMemory { get; }

    public ShellyDiscovery Discovery { get; }

    public EnergyLogger Energy { get; }

    public BridgeRuntime Runtime { get; }

    public HwinfoMonitor Monitor { get; }

    /// <summary>Gets whether settings.json could not be parsed and was renamed to settings.json.bad.</summary>
    public bool SettingsWereInvalid { get; }

    /// <summary>Creates all services and loads the settings (an unreadable file is renamed to <c>.bad</c>).</summary>
    public static AppServices Create(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var logger = loggerFactory.CreateLogger("ShellyHwinfoBridge");
        var store = SettingsStore.CreateDefault();
        var (settings, invalid) = LoadSettings(store, logger);

        var databaseLoader = new PsuDatabaseLoader(PsuDatabaseLoader.DefaultLocalPath, loggerFactory.CreateLogger<PsuDatabaseLoader>());
        var databaseUpdater = new DatabaseUpdater(databaseLoader, logger: loggerFactory.CreateLogger<DatabaseUpdater>());
        var backend = new WindowsRegistryBackend();
        var registry = new HwinfoSensorRegistry(backend);
        var autostart = new AutostartManager(backend, Environment.ProcessPath ?? Application.ExecutablePath);
        var locator = HwinfoLocator.CreateDefault(loggerFactory.CreateLogger<HwinfoLocator>());
        var sharedMemory = HwinfoSharedMemoryReader.CreateDefault(loggerFactory.CreateLogger<HwinfoSharedMemoryReader>());
        var discovery = new ShellyDiscovery(logger: loggerFactory.CreateLogger<ShellyDiscovery>());
        var energy = new EnergyLogger(new EnergyCsvStore(AppPaths.EnergyDirectory), registry, TimeProvider.System, TimeZoneInfo.Local, loggerFactory.CreateLogger<EnergyLogger>());
        var runtime = new BridgeRuntime(settings, store, databaseLoader, databaseUpdater, registry, autostart, energy, loggerFactory: loggerFactory);
        var monitor = new HwinfoMonitor(locator, sharedMemory, TimeProvider.System, loggerFactory.CreateLogger<HwinfoMonitor>(), new HwinfoGadgetRegistryReader(backend));
        return new AppServices(loggerFactory, store, databaseLoader, databaseUpdater, registry, autostart, locator, sharedMemory, discovery, energy, runtime, monitor, invalid);
    }

    public void Dispose()
    {
        Monitor.Dispose();
        DatabaseUpdater.Dispose();
    }

    private static (AppSettings? Settings, bool Invalid) LoadSettings(SettingsStore store, ILogger logger)
    {
        try
        {
            return (store.Load(), false);
        }
        catch (InvalidDataException ex)
        {
            logger.LogError(ex, "The settings file is invalid; renaming it to settings.json.bad and starting unconfigured.");
            try
            {
                File.Move(store.FilePath, store.FilePath + ".bad", overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                logger.LogError(moveError, "Renaming the invalid settings file failed.");
            }

            return (null, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Reading the settings file failed; starting unconfigured.");
            return (null, true);
        }
    }
}
