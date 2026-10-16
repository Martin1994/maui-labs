using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Renders MAUI Border with a standard <c>div</c> content container.</summary>
/// <remarks>
/// An inset shadow paints a solid stroke without adding a second CSS layout inset.
/// Rounded rectangles use border-radius; MAUI measures and arranges the child.
/// Other stroke shapes and dash patterns are not implemented.
/// </remarks>
internal sealed class BrowserBorderHandler : BrowserViewHandler<IBorderView>
{
    protected override BrowserElement CreatePlatformView()
    {
        return new("div");
    }

    private static readonly IPropertyMapper<IBorderView, BrowserBorderHandler> Properties = new PropertyMapper<IBorderView, BrowserBorderHandler>(ViewMapper)
    {
        [nameof(IContentView.Content)] = (h, _) =>
        {
            h.SetContent();
        },
        [nameof(IBorderView.Stroke)] = (h, _) =>
        {
            h.PaintBorder();
        },
        [nameof(IBorderView.StrokeThickness)] = (h, _) =>
        {
            h.PaintBorder();
        },
        [nameof(Microsoft.Maui.Controls.Border.StrokeShape)] = (h, _) =>
        {
            h.PaintBorder();
        },
        ["Shape"] = (h, _) =>
        {
            h.PaintBorder();
        },
    };
    public BrowserBorderHandler() : base(Properties)
    {
    }
    private IView? _content;
    private void SetContent()
    {
        var content = VirtualView.PresentedContent;
        if (ReferenceEquals(content, _content))
        {
            return;
        }
        if (_content is not null)
        {
            Host.Disconnect(_content);
        }
        _content = content;
        if (content is not null)
        {
            Browser.Insert(PlatformView.Id, Host.Realize(content, MauiContext!).Id, 0);
        }
        Host.RequestLayout();
    }
    private void PaintBorder()
    {
        var color = VirtualView.Stroke is SolidPaint paint ? BrowserElement.Color(paint.Color) : "transparent";
        // An inset shadow paints the stroke without adding another CSS layout inset.
        PlatformView.Style("box-shadow", $"inset 0 0 0 {BrowserElement.Number(VirtualView.StrokeThickness)}px {color}");
        if (VirtualView is Microsoft.Maui.Controls.Border { StrokeShape: RoundRectangle rounded })
        {
            var radius = rounded.CornerRadius;
            PlatformView.Style("border-radius", $"{BrowserElement.Number(radius.TopLeft)}px {BrowserElement.Number(radius.TopRight)}px {BrowserElement.Number(radius.BottomRight)}px {BrowserElement.Number(radius.BottomLeft)}px");
        }
        else
        {
            PlatformView.Style("border-radius", "0");
        }
        PlatformView.Style("overflow", "hidden");
    }
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        return VirtualView.CrossPlatformMeasure(widthConstraint, heightConstraint);
    }
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        VirtualView.CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
    }
    protected override void DisconnectHandler(BrowserElement platformView)
    {
        if (_content is not null)
        {
            Host.Disconnect(_content);
        }
        _content = null;
        base.DisconnectHandler(platformView);
    }
}