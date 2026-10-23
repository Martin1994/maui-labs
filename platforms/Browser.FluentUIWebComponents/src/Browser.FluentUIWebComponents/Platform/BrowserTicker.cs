using Microsoft.Maui.Animations;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

internal sealed class BrowserTicker : ITicker, IDisposable
{
    private int _maxFps = 60;
    private bool _disposed;
    public bool IsRunning
    {
        get;
        private set;
    }
    public bool SystemEnabled
    {
        get
        {
            return !_disposed;
        }
    }
    public Action? Fire
    {
        get;
        set;
    }
    public int MaxFps
    {
        get
        {
            return _maxFps;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            _maxFps = value;
            if (IsRunning)
            {
                // The JS operation replaces the previous rAF. If replacement
                // fails, leave a restartable stopped ticker, not a phantom one.
                IsRunning = false;
                Browser.StartTicker(value);
                IsRunning = true;
            }
        }
    }
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }
        Browser.StartTicker(MaxFps);
        IsRunning = true;
    }
    public void Stop()
    {
        IsRunning = false;
        Browser.StopTicker();
    }
    internal void Tick()
    {
        if (!_disposed && IsRunning)
        {
            Fire?.Invoke();
        }
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Fire = null;
        Stop();
    }
}