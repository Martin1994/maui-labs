using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Maps MAUI ScrollView to the native <c>maui-scroll-view</c> custom element.</summary>
/// <remarks>
/// The host itself scrolls; MAUI owns content measurement and arrangement while
/// native scroll offsets are returned to IScrollView. Only vertical/disabled
/// scrolling is implemented; programmatic scrolling fails explicitly.
/// </remarks>
internal sealed class ScrollViewHandler : BrowserViewHandler<IScrollView>
{
    protected override BrowserElement CreatePlatformView()
    {
        return new("maui-scroll-view");
    }

    private static readonly IPropertyMapper<IScrollView, ScrollViewHandler> Properties = new PropertyMapper<IScrollView, ScrollViewHandler>(ViewMapper)
    {
        [nameof(IContentView.Content)] = (h, _) =>
        {
            h.SetContent();
        },
        [nameof(IScrollView.Orientation)] = (h, v) =>
        {
            if (v.Orientation is not ScrollOrientation.Vertical and not ScrollOrientation.Neither)
            {
                throw new NotSupportedException("Only vertical browser scrolling is implemented.");
            }
            h.PlatformView.Style("overflow-y", v.Orientation == ScrollOrientation.Vertical ? "auto" : "hidden");
        },
    };
    private static readonly CommandMapper<IScrollView, ScrollViewHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(IScrollView.RequestScrollTo)] = (_, _, _) =>
        {
            throw new NotSupportedException("Programmatic scrolling is not implemented yet.");
        },
    };
    public ScrollViewHandler() : base(Properties, Commands)
    {
    }
    private IView? _content;
    private Size _desired;
    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Host.Scrolls.Add(platformView.Id, (x, y) =>
        {
            VirtualView.HorizontalOffset = x;
            VirtualView.VerticalOffset = y;
        });
        Browser.SubscribeScroll(platformView.Id);
    }
    private void SetContent()
    {
        var content = ((IContentView)VirtualView).PresentedContent;
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
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        _desired = ((IContentView)VirtualView).CrossPlatformMeasure(widthConstraint, heightConstraint);
        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, VirtualView.Width, _desired.Width, VirtualView.MinimumWidth, VirtualView.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, VirtualView.Height, _desired.Height, VirtualView.MinimumHeight, VirtualView.MaximumHeight));
    }
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        // Layout is still performed by MAUI; CSS supplies only the scrolling viewport.
        ((IContentView)VirtualView).CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
    }
    protected override void DisconnectHandler(BrowserElement platformView)
    {
        Host.Scrolls.Remove(platformView.Id);
        if (_content is not null)
        {
            Host.Disconnect(_content);
        }
        _content = null;
        base.DisconnectHandler(platformView);
    }
}