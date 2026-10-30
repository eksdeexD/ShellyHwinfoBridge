namespace ShellyHwinfoBridge;

/// <summary>
/// Marshals work from background threads (poller, monitor, coordinator) to the UI thread. Created on the UI thread;
/// posts are dropped once shutdown has begun or the handle is gone.
/// </summary>
internal sealed class UiDispatcher : Control
{
    private volatile bool _shuttingDown;

    public UiDispatcher()
    {
        Visible = false;
        CreateHandle();
    }

    /// <summary>Stops accepting posts (called first thing on exit).</summary>
    public void BeginShutdown() => _shuttingDown = true;

    /// <summary>Runs <paramref name="action"/> on the UI thread asynchronously; does nothing after shutdown began.</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_shuttingDown || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The handle was destroyed between the check and the call.
        }
    }
}

/// <summary>
/// Remembers a second-launch activation that arrives before the UI is ready. Program subscribes it to
/// <see cref="Core.Startup.SingleInstanceCoordinator.ActivationRequested"/> right after creating the coordinator;
/// the tray context attaches its handler later and receives a pending activation immediately.
/// </summary>
internal sealed class ActivationLatch
{
    private readonly object _gate = new();
    private Action? _handler;
    private bool _pending;

    /// <summary>Signals an activation (any thread).</summary>
    public void Signal()
    {
        Action? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                _pending = true;
                return;
            }
        }

        handler();
    }

    /// <summary>Attaches the handler; runs it at once if an activation is pending.</summary>
    public void Attach(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        bool pending;
        lock (_gate)
        {
            _handler = handler;
            pending = _pending;
            _pending = false;
        }

        if (pending)
        {
            handler();
        }
    }

    /// <summary>Detaches the handler (on exit).</summary>
    public void Detach()
    {
        lock (_gate)
        {
            _handler = null;
        }
    }
}
