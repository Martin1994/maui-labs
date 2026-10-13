using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserLabelHandler : BrowserViewHandler<ILabel>
{
    private static readonly IPropertyMapper<ILabel, BrowserLabelHandler> Properties = new PropertyMapper<ILabel, BrowserLabelHandler>(ViewMapper)
    {
        [nameof(ILabel.Text)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "textContent", v.Text ?? "");
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
        return new("maui-label");
    }
}