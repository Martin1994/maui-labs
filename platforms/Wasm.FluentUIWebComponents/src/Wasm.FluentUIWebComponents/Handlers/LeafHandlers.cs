using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserLabelHandler : BrowserViewHandler<ILabel>
{
    private static readonly IPropertyMapper<ILabel, BrowserLabelHandler> Properties = new PropertyMapper<ILabel, BrowserLabelHandler>(ViewMapper)
    {
        [nameof(ILabel.Text)] = (h, v) =>
        {
            Browser.Text(h.PlatformView.Id, v.Text ?? "");
            h.Host.RequestLayout();
        },
        [nameof(ILabel.TextColor)] = (h, v) =>
        {
            h.PlatformView.Style("color", BrowserElement.Color(v.TextColor));
        },
        [nameof(ILabel.Font)] = (h, v) =>
        {
            h.SetFont(v);
        },
        ["FontSize"] = (h, v) =>
        {
            h.SetFont(v);
        },
        ["FontFamily"] = (h, v) =>
        {
            h.SetFont(v);
        },
        ["FontAttributes"] = (h, v) =>
        {
            h.SetFont(v);
        },
        [nameof(ILabel.HorizontalTextAlignment)] = (h, v) =>
        {
            h.PlatformView.Style("text-align", v.HorizontalTextAlignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.End => "end",
                _ => "start"
            });
        },
    };
    public BrowserLabelHandler() : base(Properties)
    {
    }
    protected override BrowserElement CreatePlatformView()
    {
        return new("label");
    }
}

internal sealed class BrowserButtonHandler : BrowserViewHandler<IButton>
{
    private static readonly IPropertyMapper<IButton, BrowserButtonHandler> Properties = new PropertyMapper<IButton, BrowserButtonHandler>(ViewMapper)
    {
        [nameof(ITextButton.Text)] = (h, v) =>
        {
            Browser.Text(h.PlatformView.Id, ((ITextButton)v).Text ?? "");
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
    public BrowserButtonHandler() : base(Properties)
    {
    }
    protected override BrowserElement CreatePlatformView()
    {
        return new("button");
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