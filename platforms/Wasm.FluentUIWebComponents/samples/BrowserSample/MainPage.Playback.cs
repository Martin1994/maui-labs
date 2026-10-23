using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace FluentBrowserSample;

public partial class MainPage
{
    private IDispatcherTimer? _playbackTimer;
    private CancellationTokenSource? _playbackCancellation;
    private bool _playing;
    private int _playbackGeneration;
    private int _transitions;

    private void OnPlayClicked(object? sender, EventArgs e)
    {
        if (!_viewModel.CanPlay)
        {
            return;
        }
        StopPlayback("Playback started.");
        _playing = true;
        _playbackCancellation = new CancellationTokenSource();
        _transitions = 0;
        StopPlaybackButton.IsEnabled = true;
        PlaybackStatusLabel.Text = "Playing · 0 / 5 transitions";
        _playbackTimer = Dispatcher.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(650);
        // A one-shot timer is restarted only after the awaited animation finishes.
        // Slow/background browser frames therefore cannot overlap transitions.
        _playbackTimer.IsRepeating = false;
        _playbackTimer.Tick += OnPlaybackTick;
        // The first fade starts on the click. Only subsequent transitions wait.
        _ = PlayTransitionAsync();
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!_playing || !ReferenceEquals(sender, _playbackTimer))
        {
            return;
        }
        _ = PlayTransitionAsync();
    }

    private async Task PlayTransitionAsync()
    {
        int generation = _playbackGeneration;
        var cancellationToken = _playbackCancellation!.Token;
        try
        {
            await BoardItems.FadeToAsync(0, 180);
            if (!_playing || generation != _playbackGeneration)
            {
                return;
            }
            // Subscribe before Move requests layout. Its DOM insert is immediate,
            // but MAUI measures/arranges the new order on a later browser frame.
            // Keep the stack fully transparent until that arrange has completed.
            var arranged = BoardItems.WaitForArrangeAsync(cancellationToken);
            _viewModel.MoveFirstCommand.Execute(null);
            await arranged;
            if (!_playing || generation != _playbackGeneration)
            {
                return;
            }
            await BoardItems.FadeToAsync(1, 220);
            if (!_playing || generation != _playbackGeneration)
            {
                return;
            }
            PlaybackStatusLabel.Text = $"Playing · {++_transitions} / 5 transitions";
            if (_transitions == 5)
            {
                StopPlayback("Finished · 5 / 5 transitions. Your board stays in its new order.");
            }
            else
            {
                _playbackTimer?.Start();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop or a newer playback run owns the view now.
        }
        catch (Exception exception)
        {
            if (generation == _playbackGeneration)
            {
                StopPlayback("Playback could not finish. You can still edit the board.");
            }
            Console.Error.WriteLine(exception);
        }
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        StopPlayback("Playback stopped. Your board keeps its current order.");
    }

    private void StopPlayback(string message)
    {
        // Invalidate continuations before cancellation: an older run must never
        // reorder cards or restart a timer after Stop, restart or page disappearance.
        _playbackGeneration++;
        _playing = false;
        _playbackCancellation?.Cancel();
        _playbackCancellation?.Dispose();
        _playbackCancellation = null;
        if (_playbackTimer is not null)
        {
            _playbackTimer.Stop();
            _playbackTimer.Tick -= OnPlaybackTick;
            _playbackTimer = null;
        }
        BoardItems.CancelAnimations();
        BoardItems.Opacity = 1;
        StopPlaybackButton.IsEnabled = false;
        PlaybackStatusLabel.Text = message;
    }
}