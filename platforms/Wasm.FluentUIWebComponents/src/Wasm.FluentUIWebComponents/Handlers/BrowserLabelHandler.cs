using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Maps MAUI Label to Fluent's <c>fluent-text</c>.</summary>
/// <remarks>
/// Block mode, explicit newlines and wrapping preserve MAUI text layout. A relative
/// line-height supports arbitrary MAUI font sizes instead of Fluent's fixed type scale.
/// Null TextColor restores the Fluent foreground token; MAUI owns container layout.
/// </remarks>
internal sealed class BrowserLabelHandler : BrowserViewHandler<ILabel>
{
    protected override BrowserElement CreatePlatformView()
    {
        var element = new BrowserElement("fluent-text");
        Browser.SetBooleanProperty(element.Id, "block", true);
        element.Style("line-height", "1.3");
        element.Style("white-space", "pre-wrap");
        element.Style("overflow-wrap", "break-word");
        return element;
    }

    private static readonly IPropertyMapper<ILabel, BrowserLabelHandler> Properties = new PropertyMapper<ILabel, BrowserLabelHandler>(ViewMapper)
    {
        [nameof(ILabel.Text)] = (h, v) =>
        {
            Browser.SetStringProperty(h.PlatformView.Id, "textContent", v.Text ?? "");
            h.Host.RequestLayout();
        },
        [nameof(ILabel.TextColor)] = (h, v) =>
        {
            h.PlatformView.Style("color", v.TextColor is { } color ? BrowserElement.Color(color) : "var(--colorNeutralForeground1)");
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
}