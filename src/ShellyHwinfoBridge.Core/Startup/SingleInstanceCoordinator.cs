using System.Runtime.Versioning;

namespace ShellyHwinfoBridge.Core.Startup;

/// <summary>
/// Enforces a single instance per Windows session and lets a second launch ask the running instance to show its
/// settings window (the way back in when the tray icon is hidden).
/// </summary>
/// <remarks>
/// <para>
/// The first instance holds the named mutex <c>Local\&lt;name&gt;.SingleInstance</c> and waits on the auto-reset
/// event <c>Local\&lt;name&gt;.Activate</c>. A later instance finds the mutex taken, sets the event with
/// <see cref="SignalPrimary"/> and exits. The primary raises <see cref="ActivationRequested"/> on a thread-pool
/// thread; UI code must marshal to its own thread.
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
    private readonly string _eventName;
    private int _disposed;

    /// <summary>Creates the coordinator and tries to become the primary instance.</summary>
    /// <param name="name">Base name of the kernel objects; tests pass a unique name.</param>
    public SingleInstanceCoordinator(string name = AppPaths.AppName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutexName = name == AppPaths.AppName ? AppPaths.SingleInstanceMutexName : $@"Local\{name}.SingleInstance";
        _eventName = $@"Local\{name}.Activate";

        _mutex = new Mutex(initiallyOwned: false, mutexName, out var createdNew);
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
    }

    /// <summary>Raised in the primary instance when another launch asks it to show its settings window.</summary>
    public event EventHandler? ActivationRequested;

    /// <summary>Gets whether this is the first (primary) instance.</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Asks the primary instance to show its settings window. Call this from a secondary instance before exiting.
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_registration is not null)
        {
            using var done = new ManualResetEvent(false);
            if (_registration.Unregister(done))
            {
                done.WaitOne(TimeSpan.FromSeconds(5));
            }
        }

        _activateEvent?.Dispose();
        _mutex.Dispose();
    }

    private void OnActivated()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
