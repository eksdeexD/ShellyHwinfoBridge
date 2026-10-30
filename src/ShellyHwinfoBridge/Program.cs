// Copyright (c) 2026 Zapador. Licensed under the MIT License.
using System.Reflection;
using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.Logging;
using ShellyHwinfoBridge.Core.Startup;

namespace ShellyHwinfoBridge;

/// <summary>Application entry point.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Developer switches (render windows to PNG, control gallery) never touch the running instance.
        if (Diagnostics.DiagnosticsSwitches.TryRun(args, out var diagnosticsExit))
        {
            return diagnosticsExit;
        }

        using var instance = new SingleInstanceCoordinator();
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new RollingFileLoggerProvider(AppPaths.LogDirectory)));
        var logger = loggerFactory.CreateLogger("ShellyHwinfoBridge");

        if (!instance.IsPrimary)
        {
            // SPEC "Hide tray icon": a second launch asks the running instance to show its settings window.
            var signalled = instance.SignalPrimary();
            logger.LogInformation("Another instance is already running; {Result}.", signalled ? "asked it to show settings" : "could not signal it");
            return 0;
        }

        var startup = StartupArguments.Parse(args);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        logger.LogInformation("ShellyHwinfoBridge {Version} starting (autostart: {Autostart}).", version, startup.Autostart);

        // The UI (milestone 3) subscribes here and shows the settings window on its UI thread.
        instance.ActivationRequested += (_, _) => logger.LogInformation("Second launch requested the settings window.");

        logger.LogWarning("UI not yet implemented; exiting.");
        return 0;
    }
}
