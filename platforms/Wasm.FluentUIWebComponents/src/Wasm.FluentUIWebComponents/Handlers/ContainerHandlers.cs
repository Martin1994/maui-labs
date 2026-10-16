using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Hosts a MAUI content page in a standard <c>div</c>.</summary>
/// <remarks>Delegates content measurement/arrangement to MAUI and disconnects replaced content.</remarks>
internal sealed class BrowserPageHandler : BrowserViewHandler<IContentView>
{
    protected override BrowserElement CreatePlatformView()
    {
        return new("div");
    }

    private static readonly IPropertyMapper<IContentView, BrowserPageHandler> Properties = new PropertyMapper<IContentView, BrowserPageHandler>(ViewMapper)
    {
        [nameof(IContentView.Content)] = (h, _) =>
        {
            h.SetContent();
        },
    };
    public BrowserPageHandler() : base(Properties)
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

/// <summary>Hosts MAUI layouts, including Grid and stacks, in a standard <c>div</c>.</summary>
/// <remarks>
/// Uses MAUI's layout manager, not CSS Grid/Flexbox. Applies constraints before
/// measuring children, and maps collection updates to child creation/release and DOM order.
/// </remarks>
internal sealed class BrowserLayoutHandler : BrowserViewHandler<ILayout>, ILayoutHandler
{
    protected override BrowserElement CreatePlatformView()
    {
        return new("div");
    }

    private readonly List<IView> _children = new();
    object ILayoutHandler.PlatformView
    {
        get
        {
            return PlatformView;
        }
    }
    private static readonly CommandMapper<ILayout, BrowserLayoutHandler> Commands = new(ViewCommandMapper)
    {
        [nameof(ILayoutHandler.Add)] = (h, _, value) =>
        {
            h.Add(((LayoutHandlerUpdate)value!).View);
        },
        [nameof(ILayoutHandler.Insert)] = (h, _, value) =>
        {
            var update = (LayoutHandlerUpdate)value!;
            h.Insert(update.Index, update.View);
        },
        [nameof(ILayoutHandler.Update)] = (h, _, value) =>
        {
            var update = (LayoutHandlerUpdate)value!;
            h.Update(update.Index, update.View);
        },
        [nameof(ILayoutHandler.Remove)] = (h, _, value) =>
        {
            h.Remove(((LayoutHandlerUpdate)value!).View);
        },
        [nameof(ILayoutHandler.Clear)] = (h, _, _) =>
        {
            h.Clear();
        },
        [nameof(ILayoutHandler.UpdateZIndex)] = (h, _, value) =>
        {
            h.UpdateZIndex((IView)value!);
        },
    };
    public BrowserLayoutHandler() : base(ViewMapper, Commands)
    {
    }

    public override void SetVirtualView(IView view)
    {
        if (ReferenceEquals(((IElementHandler)this).VirtualView, view))
        {
            return;
        }
        Clear();
        base.SetVirtualView(view);
        foreach (var child in (ILayout)view)
        {
            Add(child);
        }
    }

    public void Add(IView child)
    {
        Insert(_children.Count, child);
    }
    public void Insert(int index, IView child)
    {
        var element = Host.Realize(child, MauiContext!);
        _children.Insert(index, child);
        Browser.Insert(PlatformView.Id, element.Id, index);
        Host.RequestLayout();
    }
    public void Remove(IView child)
    {
        if (!_children.Remove(child))
        {
            return;
        }
        Host.Disconnect(child);
        Host.RequestLayout();
    }
    public void Update(int index, IView child)
    {
        var previous = _children[index];
        Remove(previous);
        Insert(index, child);
    }
    public void Clear()
    {
        foreach (var child in _children.ToArray())
        {
            Remove(child);
        }
    }
    public void UpdateZIndex(IView child)
    {
        if (child.Handler?.PlatformView is BrowserElement element)
        {
            element.Style("z-index", BrowserElement.Number(child.ZIndex));
        }
    }
    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        // Constrain children before measuring, not just the container's returned size.
        // Otherwise a max-width layout measures text wider than it will be arranged.
        widthConstraint = LayoutManager.ResolveConstraints(widthConstraint, VirtualView.Width,
            widthConstraint, VirtualView.MinimumWidth, VirtualView.MaximumWidth);
        heightConstraint = LayoutManager.ResolveConstraints(heightConstraint, VirtualView.Height,
            heightConstraint, VirtualView.MinimumHeight, VirtualView.MaximumHeight);
        return ((ICrossPlatformLayout)VirtualView).CrossPlatformMeasure(widthConstraint, heightConstraint);
    }
    public override void PlatformArrange(Rect frame)
    {
        base.PlatformArrange(frame);
        ((ICrossPlatformLayout)VirtualView).CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
    }
    protected override void DisconnectHandler(BrowserElement platformView)
    {
        Clear();
        base.DisconnectHandler(platformView);
    }
}