using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Shared MAUI view mapping for numeric-handle browser elements.</summary>
/// <remarks>
/// Concrete handlers choose their component. This base maps common CSS properties,
/// measures intrinsic leaf content at its effective width, and applies MAUI frames
/// in CSS pixels. It does not implement a second browser layout engine.
/// </remarks>
internal abstract class BrowserViewHandler<TView> : ViewHandler<TView, BrowserElement> where TView : class, IView
{
    protected BrowserApplication Host
    {
        get
        {
            return MauiContext!.Services.GetRequiredService<BrowserApplication>();
        }
    }
    public static new readonly IPropertyMapper<IView, BrowserViewHandler<TView>> ViewMapper =
        new PropertyMapper<IView, BrowserViewHandler<TView>>
        {
            [nameof(IView.AutomationId)] = (h, v) =>
            {
                Browser.SetAttribute(h.PlatformView.Id, "data-automation-id", v.AutomationId);
            },
            [nameof(IView.Opacity)] = (h, v) =>
            {
                h.PlatformView.Style("opacity", BrowserElement.Number(v.Opacity));
            },
            [nameof(IView.ZIndex)] = (h, v) =>
            {
                h.PlatformView.Style("z-index", BrowserElement.Number(v.ZIndex));
            },
            [nameof(IView.Visibility)] = MapVisibility,
            [nameof(VisualElement.IsVisible)] = MapVisibility,
            [nameof(IView.Background)] = MapBackground,
            [nameof(VisualElement.BackgroundColor)] = MapBackground,
            [nameof(IView.InputTransparent)] = (h, v) =>
            {
                h.PlatformView.Style("pointer-events", v.InputTransparent ? "none" : "auto");
            },
            [nameof(IView.Width)] = (h, _) =>
            {
                h.Host.RequestLayout();
            },
            [nameof(IView.Height)] = (h, _) =>
            {
                h.Host.RequestLayout();
            },
            [nameof(VisualElement.WidthRequest)] = (h, _) =>
            {
                h.Host.RequestLayout();
            },
            [nameof(VisualElement.HeightRequest)] = (h, _) =>
            {
                h.Host.RequestLayout();
            },
        };

    public static new readonly CommandMapper<IView, BrowserViewHandler<TView>> ViewCommandMapper = new()
    {
        [nameof(IView.ZIndex)] = (h, v, _) =>
        {
            h.PlatformView.Style("z-index", BrowserElement.Number(v.ZIndex));
        },
        [nameof(IView.InvalidateMeasure)] = (h, _, _) =>
        {
            h.Host.RequestLayout();
        },
    };

    protected BrowserViewHandler(IPropertyMapper mapper, CommandMapper? commands = null)
        : base(mapper, commands ?? ViewCommandMapper)
    {
    }

    private static void MapVisibility(BrowserViewHandler<TView> handler, IView view)
    {
        handler.PlatformView.Style("display", view.Visibility == Visibility.Collapsed ? "none" : "");
        handler.PlatformView.Style("visibility", view.Visibility == Visibility.Hidden ? "hidden" : "visible");
        handler.Host.RequestLayout();
    }

    private static void MapBackground(BrowserViewHandler<TView> handler, IView view)
    {
        handler.PlatformView.Style("background-color", view.Background is SolidPaint paint ? BrowserElement.Color(paint.Color) : "");
    }

    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Host.Track(platformView, this);
    }

    protected override void DisconnectHandler(BrowserElement platformView)
    {
        Host.Untrack(platformView);
        Browser.Release(platformView.Id);
        base.DisconnectHandler(platformView);
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        double measuringWidth = LayoutManager.ResolveConstraints(widthConstraint, VirtualView.Width,
            widthConstraint, VirtualView.MinimumWidth, VirtualView.MaximumWidth);
        double naturalWidth = Browser.Measure(PlatformView.Id, measuringWidth, heightConstraint, false);
        double width = LayoutManager.ResolveConstraints(widthConstraint, VirtualView.Width,
            naturalWidth, VirtualView.MinimumWidth, VirtualView.MaximumWidth);
        // Height must be measured at the effective requested/max width, not the parent's width.
        // An explicit height supersedes intrinsic height in ResolveConstraints.
        // Keep the first measurement (including input flushing), but avoid a second
        // DOM write/read/restore cycle whose result would be discarded.
        double height = Microsoft.Maui.Primitives.Dimension.IsExplicitSet(VirtualView.Height)
            ? VirtualView.Height
            : Browser.Measure(PlatformView.Id, width, heightConstraint, true);
        return new Size(
            width,
            LayoutManager.ResolveConstraints(heightConstraint, VirtualView.Height, height, VirtualView.MinimumHeight, VirtualView.MaximumHeight));
    }

    public override void PlatformArrange(Rect frame)
    {
        PlatformView.Frame(frame);
    }

    protected void SetFont(ITextStyle view)
    {
        var font = view.Font;
        var services = MauiContext!.Services;
        var fontManager = services.GetRequiredService<IFontManager>();
        PlatformView.Style("font-size", BrowserElement.Number(font.Size > 0 ? font.Size : fontManager.DefaultFontSize) + "px");
        PlatformView.Style("font-family", BrowserFontManager.ResolveFamily(font.Family, services.GetRequiredService<IFontRegistrar>()));
        PlatformView.Style("font-weight", BrowserElement.Number((int)font.Weight));
        PlatformView.Style("font-style", font.Slant == FontSlant.Italic ? "italic" : "normal");
        Host.RequestLayout();
    }
}