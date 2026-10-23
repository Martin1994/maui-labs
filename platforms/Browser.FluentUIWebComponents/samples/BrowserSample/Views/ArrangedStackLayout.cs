using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace FluentBrowserSample.Views;

/// <summary>A stack that can wait for its children to finish their next arrange pass.</summary>
public sealed class ArrangedStackLayout : VerticalStackLayout
{
    private event EventHandler? Arranged;

    protected override ILayoutManager CreateLayoutManager() => new NotifyingLayoutManager(this);

    public async Task WaitForArrangeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Continue outside the layout call so an animation cannot mutate the
        // tree reentrantly while MAUI is still arranging its ancestors.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnArranged(object? sender, EventArgs args) => completion.TrySetResult();

        Arranged += OnArranged;
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await completion.Task;
        }
        finally
        {
            Arranged -= OnArranged;
        }
    }

    private sealed class NotifyingLayoutManager(ArrangedStackLayout layout) : VerticalStackLayoutManager(layout)
    {
        public override Size ArrangeChildren(Rect bounds)
        {
            var size = base.ArrangeChildren(bounds);
            layout.Arranged?.Invoke(layout, EventArgs.Empty);
            return size;
        }
    }
}