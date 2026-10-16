using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserDispatcher : IDispatcher, IDispatcherProvider, IDisposable
{
    private readonly Dictionary<int, Action> _pending = new();
    private readonly HashSet<BrowserDispatcherTimer> _timers = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private int _sequence;
    private bool _disposed;

    public bool IsDispatchRequired
    {
        get
        {
            return Environment.CurrentManagedThreadId != _threadId;
        }
    }
    public IDispatcher? GetForCurrentThread()
    {
        return IsDispatchRequired ? null : this;
    }
    public bool Dispatch(Action action)
    {
        return DispatchDelayed(TimeSpan.Zero, action);
    }
    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        return Schedule(delay, action) != 0;
    }

    internal int Schedule(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_disposed)
        {
            return 0;
        }
        if (IsDispatchRequired)
        {
            throw new NotSupportedException("The browser backend currently requires one UI thread.");
        }
        int id = checked(++_sequence);
        _pending.Add(id, action);
        try
        {
            Browser.Schedule(id, Math.Clamp(delay.TotalMilliseconds, 0, int.MaxValue));
        }
        catch
        {
            _pending.Remove(id);
            throw;
        }
        return id;
    }

    public IDispatcherTimer CreateTimer()
    {
        VerifyAccess();
        return new BrowserDispatcherTimer(this);
    }
    internal void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsDispatchRequired)
        {
            throw new NotSupportedException("The browser backend currently requires one UI thread.");
        }
    }
    internal void TrackTimer(BrowserDispatcherTimer timer)
    {
        VerifyAccess();
        _timers.Add(timer);
    }
    internal void UntrackTimer(BrowserDispatcherTimer timer)
    {
        _timers.Remove(timer);
    }
    internal void Cancel(int id)
    {
        if (_pending.Remove(id))
        {
            Browser.CancelSchedule(id);
        }
    }
    public void Run(int id)
    {
        if (!_disposed && _pending.Remove(id, out var action))
        {
            action();
        }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        foreach (var timer in _timers.ToArray())
        {
            timer.Stop();
        }
        _disposed = true;
        foreach (int id in _pending.Keys)
        {
            Browser.CancelSchedule(id);
        }
        _pending.Clear();
    }
}