using System.Security;
using ShellyHwinfoBridge.Core.Hwinfo;

namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>
/// Runs the one-shot <see cref="StartupCommand"/>s (<c>--exit</c>, <c>--remove-sensors</c>) without starting the
/// tray app, and maps the outcome to a process exit code.
/// </summary>
/// <remarks>
/// <c>--remove-sensors</c> first asks a running instance to exit: a running instance writes its values every poll
/// and would recreate the keys right after they were deleted.
/// </remarks>
public sealed class MaintenanceCommandRunner
{
    /// <summary>Exit code: done (or nothing to do).</summary>
    public const int Success = 0;

    /// <summary>Exit code: a running instance could not be asked to exit, or did not exit in time.</summary>
    public const int InstanceStillRunning = 1;

    /// <summary>Exit code: deleting the HWiNFO sensor group failed.</summary>
    public const int RemoveFailed = 2;

    /// <summary>How long to wait for a running instance to exit (its clean exit takes about 3 s at most).</summary>
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<TimeSpan, ExitRequestResult> _requestExit;
    private readonly IHwinfoSensorRegistry _sensors;

    /// <summary>Creates a runner.</summary>
    /// <param name="requestExit">Asks a running instance to exit and waits up to the given time
    /// (normally <see cref="SingleInstanceCoordinator.RequestExit"/>).</param>
    /// <param name="sensors">The HWiNFO sensor registry of the current user.</param>
    public MaintenanceCommandRunner(Func<TimeSpan, ExitRequestResult> requestExit, IHwinfoSensorRegistry sensors)
    {
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        _sensors = sensors ?? throw new ArgumentNullException(nameof(sensors));
    }

    /// <summary>Runs <paramref name="command"/>.</summary>
    /// <param name="command">The command; <see cref="StartupCommand.None"/> does nothing.</param>
    /// <returns>The process exit code: <see cref="Success"/>, <see cref="InstanceStillRunning"/> or <see cref="RemoveFailed"/>.</returns>
    public int Run(StartupCommand command)
    {
        if (command == StartupCommand.None)
        {
            return Success;
        }

        var exit = _requestExit(ExitTimeout);
        var stillRunning = exit is ExitRequestResult.NotSupported or ExitRequestResult.TimedOut;
        if (command == StartupCommand.Exit)
        {
            return stillRunning ? InstanceStillRunning : Success;
        }

        try
        {
            _sensors.RemoveAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return RemoveFailed;
        }

        return stillRunning ? InstanceStillRunning : Success;
    }
}
