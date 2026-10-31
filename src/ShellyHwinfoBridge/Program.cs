// Copyright (c) 2026 Zapador. Licensed under the MIT License.
using System.Reflection;
using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.Hwinfo;
using ShellyHwinfoBridge.Core.Logging;
using ShellyHwinfoBridge.Core.Startup;
using ShellyHwinfoBridge.Theming;
using ShellyHwinfoBridge.Windows;

namespace ShellyHwinfoBridge;

/// <summary>Application entry point.</summary>
internal static class Program
{
    private static ILogger? s_logger;

    [STAThread]
    private static int Main(string[] args)
    {
        // Developer switches (render windows to PNG, control gallery) never touch the running instance.
        if (Diagnostics.DiagnosticsSwitches.TryRun(args, out var diagnosticsExit))
        {
            return diagnosticsExit;
        }

        // --exit and --remove-sensors are one-shot commands: they never start the tray app or create the mutex.
        var startup = StartupArguments.Parse(args);
        if (startup.Command != StartupCommand.None)
        {
            return RunCommand(startup.Command);
        }

        using var instance = new SingleInstanceCoordinator();

        // Latch a second launch (and an exit request from --exit or the installer) at once, so one that arrives
        // while we start up is not lost.
        var activation = new ActivationLatch();
        instance.ActivationRequested += (_, _) => activation.Signal();
        var exitRequest = new ActivationLatch();
        instance.ExitRequested += (_, _) => exitRequest.Signal();

        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new RollingFileLoggerProvider(AppPaths.LogDirectory)));
        var logger = loggerFactory.CreateLogger("ShellyHwinfoBridge");
        s_logger = logger;

        if (!instance.IsPrimary)
        {
            // SPEC "Hide tray icon": a second launch asks the running instance to show its main window.
            NativeMethods.AllowAnyProcessToSetForeground();
            var signalled = instance.SignalPrimary();
            logger.LogInformation("Another instance is already running; {Result}.", signalled ? "asked it to show the main window" : "could not signal it");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(ThemeFonts.CreateApplicationDefault());
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        logger.LogInformation("ShellyHwinfoBridge {Version} starting (autostart: {Autostart}, minimized: {Minimized}).", version, startup.Autostart, startup.Minimized);

        using var services = AppServices.Create(loggerFactory);
        var notices = new List<string>();
        if (services.SettingsWereInvalid)
        {
            notices.Add("The settings could not be read (the file was renamed to settings.json.bad). Run the setup wizard again.");
        }

        // Create or repair the HWiNFO sensor keys on every start.
        try
        {
            if (services.Registry.EnsureKeys())
            {
                logger.LogInformation("HWiNFO sensor keys were created or repaired.");
                if (services.Runtime.Settings is not null)
                {
                    notices.Add("Restart HWiNFO to show the Shelly Power sensors.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogError(ex, "Creating the HWiNFO sensor keys failed.");
        }

        // The context subscribes to the runtime before polling starts, so the first reading is not missed.
        using var context = new TrayApplicationContext(services, activation, exitRequest, startup, logger, notices);
        services.Runtime.Start();
        services.Monitor.Start();
        RepairAutostart(services, logger);

        Application.Run(context);
        logger.LogInformation("ShellyHwinfoBridge stopped.");
        return 0;
    }

    /// <summary>Runs a maintenance command for the current user (see <see cref="MaintenanceCommandRunner"/>).</summary>
    private static int RunCommand(StartupCommand command)
    {
        var sensors = new HwinfoSensorRegistry(new WindowsRegistryBackend());
        var runner = new MaintenanceCommandRunner(timeout => SingleInstanceCoordinator.RequestExit(timeout), sensors);
        return runner.Run(command);
    }

    private static void RepairAutostart(AppServices services, ILogger logger)
    {
        try
        {
            // SPEC "Installer": write the current exe path on every start while autostart is on, so the entry follows
            // the exe (e.g. from a portable folder into Program Files) and is restored if it went missing.
            if (services.Runtime.Settings is { StartWithWindows: true } && services.Autostart.EnsureCurrent())
            {
                logger.LogInformation("Updated the Windows startup entry to {Path}.", services.Autostart.ExecutablePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.LogError(ex, "Repairing the Windows startup entry failed.");
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        s_logger?.LogError(e.Exception, "Unhandled UI error.");
        try
        {
            ThemedDialog.Show(null, "Something went wrong", e.Exception.Message + "\n\nThe details are in the log. The app keeps running.");
        }
        catch (Exception ex)
        {
            // Never let the error dialog itself bring the app down; both errors are logged.
            s_logger?.LogError(ex, "Showing the error dialog failed.");
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        s_logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled error (terminating: {Terminating}).", e.IsTerminating);
}
