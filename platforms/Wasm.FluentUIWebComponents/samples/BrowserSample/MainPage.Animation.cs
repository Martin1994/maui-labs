using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage
{
    private bool _animating;
    private bool _animationStopped;

    private async void OnAnimateClicked(object? sender, EventArgs e)
    {
        if (_animating)
        {
            return;
        }
        _animating = true;
        try
        {
            AnimationStatusLabel.Text = "Animation: running";
            await AnimationPreview.FadeToAsync(0.25, 250);
            if (_animationStopped)
            {
                return;
            }
            await AnimationPreview.FadeToAsync(1, 250);
            if (_animationStopped)
            {
                return;
            }
            AnimationStatusLabel.Text = "Animation: completed";
        }
        catch (Exception exception)
        {
            AnimationStatusLabel.Text = "Animation: failed";
            Console.Error.WriteLine(exception);
        }
        finally
        {
            _animating = false;
        }
    }

    private void StopAnimation()
    {
        _animationStopped = true;
        AnimationPreview.CancelAnimations();
    }
}