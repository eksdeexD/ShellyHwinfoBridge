// Copyright (c) 2026 Zapador. Licensed under the MIT License.
using System.Reflection;
using Microsoft.Extensions.Logging;
using ShellyHwinfoBridge.Core;
using ShellyHwinfoBridge.Core.Logging;

namespace ShellyHwinfoBridge;

/// <summary>Application entry point.</summary>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, AppPaths.SingleInstanceMutexName, out var createdNew);
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddProvider(new RollingFileLoggerProvider(AppPaths.LogDirectory)));
        var logger = loggerFactory.CreateLogger("ShellyHwinfoBridge");

        if (!createdNew)
        {
            // Milestone 1: signalling the running instance to show settings is not implemented yet.
            logger.LogInformation("Another instance is already running; exiting.");
            return 0;
        }

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            logger.LogInformation("ShellyHwinfoBridge {Version} starting.", version);
            logger.LogWarning("UI not yet implemented; exiting.");
            return 0;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
