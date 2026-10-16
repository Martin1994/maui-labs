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
        try
        {
            Queue();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void Queue()
    {
        long generation = _generation;
        _pending = _dispatcher.Schedule(_interval, () =>
        {
            if (!IsRunning || generation != _generation)
            {
                return;
            }
            _pending = 0;
            if (!IsRepeating)
            {
                Stop();
            }
            try
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                // Stop/restart or Interval changes from a Tick handler own their
                // new schedule; never add a second callback for that generation.
                if (IsRunning && generation == _generation)
                {
                    Queue();
                }
            }
        });
    }

    public void Stop()
    {
        IsRunning = false;
        _generation++;
        _dispatcher.Cancel(_pending);
        _pending = 0;
        _dispatcher.UntrackTimer(this);
    }
}