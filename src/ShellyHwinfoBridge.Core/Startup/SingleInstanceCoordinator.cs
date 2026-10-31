using System.Runtime.Versioning;

namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>
/// Enforces a single instance per Windows session and lets a second launch ask the running instance to show its
/// main window (the way back in when the tray icon is hidden), or to exit (used by <c>--exit</c>,
/// <c>--remove-sensors</c> and the installer).
/// </summary>
/// <remarks>
/// <para>
/// The first instance holds the named mutex <c>Local\&lt;name&gt;.SingleInstance</c> and waits on the auto-reset
/// event <c>Local\&lt;name&gt;.Activate</c>. A later instance finds the mutex taken, sets the event with
/// <see cref="SignalPrimary"/> and exits. The primary raises <see cref="ActivationRequested"/> on a thread-pool
/// thread; UI code must marshal to its own thread.
/// </para>
/// <para>
/// The primary also waits on the auto-reset event <c>Local\&lt;name&gt;.Exit</c>. <see cref="RequestExit"/> sets it
/// (the installer sets the same event by name from its script) and the primary raises <see cref="ExitRequested"/>
/// and runs its clean exit.
/// </para>
/// <para>
/// The mutex is held by handle, not by ownership, so there is no thread affinity; it disappears when the primary
/// disposes the coordinator or its process ends.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activateEvent;
    private readonly RegisteredWaitHandle? _registration;
    private readonly EventWaitHandle? _exitEvent;
    private readonly RegisteredWaitHandle? _exitRegistration;
    private readonly string _eventName;
    private int _disposed;

    /// <summary>Creates the coordinator and tries to become the primary instance.</summary>
    /// <param name="name">Base name of the kernel objects; tests pass a unique name.</param>
    public SingleInstanceCoordinator(string name = AppPaths.AppName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _eventName = $@"Local\{name}.Activate";

        _mutex = new Mutex(initiallyOwned: false, MutexNameFor(name), out var createdNew);
        IsPrimary = createdNew;
        if (!IsPrimary)
        {
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => OnActivated(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventNameFor(name));
        _exitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _exitEvent,
            (_, _) => OnExitRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Raised in the primary instance when another launch asks it to show its main window.</summary>
    public event EventHandler? ActivationRequested;

    /// <summary>
    /// Raised in the primary instance, on a thread-pool thread, when <see cref="RequestExit"/> or the installer asks
    /// it to exit cleanly.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>Gets whether this is the first (primary) instance.</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Asks the primary instance to show its main window. Call this from a secondary instance before exiting.
    /// </summary>
    /// <returns><c>true</c> if the primary's event was found and set.</returns>
    public bool SignalPrimary()
    {
        if (IsPrimary)
        {
            return false;
        }

        // The primary may still be starting up; retry briefly before giving up.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (EventWaitHandle.TryOpenExisting(_eventName, out var handle))
            {
                using (handle)
                {
                    return handle.Set();
                }
            }

            Thread.Sleep(100);
        }

        return false;
    }

    /// <summary>
    /// Asks the running primary instance to exit cleanly and waits until it has released its single-instance mutex.
    /// Call it without creating a coordinator first (a coordinator of your own would keep the mutex alive).
    /// </summary>
    /// <param name="timeout">How long to wait for the primary to exit.</param>
    /// <param name="name">Base name of the kernel objects; tests pass a unique name.</param>
    /// <returns>What happened; see <see cref="ExitRequestResult"/>.</returns>
    public static ExitRequestResult RequestExit(TimeSpan timeout, string name = AppPaths.AppName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutexName = MutexNameFor(name);
        if (!IsHeld(mutexName))
        {
            return ExitRequestResult.NotRunning;
        }

        if (!EventWaitHandle.TryOpenExisting(ExitEventNameFor(name), out var exitEvent))
        {
            // An older version without the exit event, or one that has just exited.
            return IsHeld(mutexName) ? ExitRequestResult.NotSupported : ExitRequestResult.NotRunning;
        }

        using (exitEvent)
        {
            exitEvent.Set();
        }

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (IsHeld(mutexName))
        {
            if (Environment.TickCount64 >= deadline)
            {
                return ExitRequestResult.TimedOut;
            }

            Thread.Sleep(100);
        }

        return ExitRequestResult.Exited;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Unregister(_registration);
        Unregister(_exitRegistration);
        _activateEvent?.Dispose();
        _exitEvent?.Dispose();
        _mutex.Dispose();
    }

    private static string MutexNameFor(string name) =>
        name == AppPaths.AppName ? AppPaths.SingleInstanceMutexName : $@"Local\{name}.SingleInstance";

    private static string ExitEventNameFor(string name) =>
        name == AppPaths.AppName ? AppPaths.ExitEventName : $@"Local\{name}.Exit";

    private static bool IsHeld(string mutexName)
    {
        if (!Mutex.TryOpenExisting(mutexName, out var mutex))
        {
            return false;
        }

        mutex.Dispose();
        return true;
    }

    private static void Unregister(RegisteredWaitHandle? registration)
    {
        if (registration is null)
        {
            return;
        }

        using var done = new ManualResetEvent(false);
        if (registration.Unregister(done))
        {
            done.WaitOne(TimeSpan.FromSeconds(5));
        }
    }

    private void OnActivated()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnExitRequested()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>The outcome of <see cref="SingleInstanceCoordinator.RequestExit"/>.</summary>
public enum ExitRequestResult
{
    /// <summary>No instance was running.</summary>
    NotRunning,

    /// <summary>The running instance exited within the timeout.</summary>
    Exited,

    /// <summary>An instance is running but has no exit event (an older version), so it could not be asked to exit.</summary>
    NotSupported,

    /// <summary>The running instance was asked to exit but still held its mutex when the timeout elapsed.</summary>
    TimedOut,
}
