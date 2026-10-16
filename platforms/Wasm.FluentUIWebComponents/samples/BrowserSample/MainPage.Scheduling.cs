using Microsoft.Maui.Dispatching;

namespace FluentBrowserSample;

public partial class MainPage
{
    private IDispatcherTimer? _demoTimer;
    private int _ticks;

    private void OnTimerClicked(object? sender, EventArgs e)
    {
        if (_demoTimer is null)
        {
            _demoTimer = Dispatcher.CreateTimer();
            _demoTimer.Interval = TimeSpan.FromMilliseconds(150);
            _demoTimer.Tick += OnTimerTick;
        }
        _demoTimer.Stop();
        _ticks = 0;
        TimerStatusLabel.Text = "Timer: 0";
        _demoTimer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        TimerStatusLabel.Text = $"Timer: {++_ticks}";
        if (_ticks == 5)
        {
            StopTimer();
            TimerStatusLabel.Text = "Timer: 5 (stopped)";
        }
    }

    private void StopTimer()
    {
        _demoTimer?.Stop();
    }
}