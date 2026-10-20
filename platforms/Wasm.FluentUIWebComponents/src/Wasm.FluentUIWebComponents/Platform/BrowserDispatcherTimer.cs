using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserDispatcherTimer : IDispatcherTimer
{
    private readonly BrowserDispatcher _dispatcher;
    private TimeSpan _interval;
    private int _pending;
    private long _generation;

    internal BrowserDispatcherTimer(BrowserDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public TimeSpan Interval
    {
        get
        {
            return _interval;
        }
        set
        {
            _dispatcher.VerifyAccess();
            if (value < TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (_interval == value)
            {
                return;
            }
            _interval = value;
            if (IsRunning)
            {
                Stop();
                Start();
            }
        }
    }

    public bool IsRepeating
    {
        get;
        set;
    } = true;
    public bool IsRunning
    {
        get;
        private set;
    }
    public event EventHandler? Tick;

    public void Start()
    {
        _dispatcher.VerifyAccess();
        if (IsRunning)
        {
            return;
        }
        IsRunning = true;
        _generation++;
        _dispatcher.TrackTimer(this);
        Queue();
    }

    private void Queue()
    {
        long generation = _generation;
        try
        {
            _pending = _dispatcher.Schedule(_interval, () => OnTick(generation));
        }
        catch
        {
            // Schedule rolls back its registration on failure. Do not retain a
            // running timer without a callback, including after the first tick.
            Stop();
            throw;
        }
    }

    private void OnTick(long generation)
    {
        if (!IsRunning || generation != _generation)
        {
            return;
        }
        _pending = 0;
        try
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A failing callback stops its own generation. An explicit restart
            // from that callback owns a new schedule and must not be cancelled.
            if (generation == _generation)
            {
                Stop();
            }
            throw;
        }
        // Read IsRepeating AFTER user code. Stop/restart and Interval changes
        // own their new schedule; never enqueue twice for that generation.
        if (IsRunning && generation == _generation)
        {
            if (IsRepeating)
            {
                Queue();
            }
            else
            {
                Stop();
            }
        }
    }

    public void Stop()
    {
        IsRunning = false;
        _generation++;
        int pending = _pending;
        _pending = 0;
        _dispatcher.UntrackTimer(this);
        _dispatcher.Cancel(pending);
    }
}