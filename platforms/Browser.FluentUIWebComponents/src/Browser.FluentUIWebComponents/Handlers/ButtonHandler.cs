using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Maps MAUI Button to Fluent's <c>fluent-button</c>.</summary>
/// <remarks>
/// Keeps Fluent's neutral theme/state styling unless MAUI supplies explicit colors.
/// Native clicks synchronously invoke IButton.Clicked, preserving user activation;
/// disconnecting removes the managed callback and releases the DOM subscription.
/// </remarks>
internal sealed class ButtonHandler : BrowserViewHandler<IButton>
{
    protected override BrowserElement CreatePlatformView()
    {
        return new("fluent-button");
    }

    private static readonly IPropertyMapper<IButton, ButtonHandler> Properties = new PropertyMapper<IButton, ButtonHandler>(ViewMapper)
    {
        [nameof(IView.IsEnabled)] = (h, v) =>
        {
            Browser.SetBooleanProperty(h.PlatformView.Id, "disabled", !v.IsEnabled);
        },
        [nameof(ITextButton.Text)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "textContent", ((ITextButton)v).Text ?? "");
            h.Host.RequestLayout();
        },
        [nameof(ITextStyle.Font)] = (h, v) =>
        {
            h.SetFont((ITextStyle)v);
        },
        ["FontSize"] = (h, v) =>
        {
            h.SetFont((ITextStyle)v);
        },
        ["FontFamily"] = (h, v) =>
        {
            h.SetFont((ITextStyle)v);
        },
        ["FontAttributes"] = (h, v) =>
        {
            h.SetFont((ITextStyle)v);
        },
        [nameof(ITextButton.TextColor)] = (h, v) =>
        {
            h.PlatformView.Style("color",
                ((ITextButton)v).TextColor is { } color ? BrowserElement.Color(color) : "");
        },
        [nameof(IButton.Padding)] = (h, v) =>
        {
            var p = v.Padding;
            if (double.IsFinite(p.Left) && double.IsFinite(p.Top) && double.IsFinite(p.Right) && double.IsFinite(p.Bottom))
            {
                h.PlatformView.Style("padding", $"{BrowserElement.Number(p.Top)}px {BrowserElement.Number(p.Right)}px {BrowserElement.Number(p.Bottom)}px {BrowserElement.Number(p.Left)}px");
            }
            h.Host.RequestLayout();
        },
    };
    public ButtonHandler() : base(Properties)
    {
    }
    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Host.Clicks.Add(platformView.Id, () =>
        {
            if (VirtualView.IsEnabled)
            {
                VirtualView.Clicked();
            }
        });
        Browser.SubscribeClick(platformView.Id);
    }
    protected override void DisconnectHandler(BrowserElement platformView)
    {
        Host.Clicks.Remove(platformView.Id);
        base.DisconnectHandler(platformView);
    }
}