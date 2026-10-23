using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Displays a catalog file inside an explicitly sized MAUI image frame.</summary>
internal sealed class BrowserImageHandler : BrowserViewHandler<IImage>
{
    private sealed record ImageRequest(int Generation, IImage View, BrowserElement Element,
        IImageSource? Source, string? Filename, bool HasSize)
    {
        public bool Pending { get; set; }
    }

    private int _generation;
    private ImageRequest? _request;
    private bool _settingVirtualView;

    private static readonly IPropertyMapper<IImage, BrowserImageHandler> Properties =
        new PropertyMapper<IImage, BrowserImageHandler>(ViewMapper)
        {
            [nameof(IImageSourcePart.Source)] = (h, _) => h.UpdateSource(),
            [nameof(IImage.Aspect)] = (h, v) => Browser.ImageAspect(h.PlatformView.Id, v.Aspect.ToString()),
            [nameof(IView.Width)] = MapSize,
            [nameof(IView.Height)] = MapSize,
            [nameof(VisualElement.WidthRequest)] = MapSize,
            [nameof(VisualElement.HeightRequest)] = MapSize,
            [nameof(IView.Semantics)] = (h, v) =>
            {
                Browser.SetAttribute(h.PlatformView.Id, "role", "img");
                Browser.SetAttribute(h.PlatformView.Id, "aria-label", v.Semantics?.Description);
            },
        };

    public BrowserImageHandler() : base(Properties)
    {
    }

    public override void SetVirtualView(IView view)
    {
        bool wasSettingVirtualView = _settingVirtualView;
        _settingVirtualView = true;
        try
        {
            base.SetVirtualView(view);
        }
        finally
        {
            _settingVirtualView = wasSettingVirtualView;
        }
        // Defer IsLoading notifications until the initial mapper pass finishes.
        // App code can disconnect us there; later base mappings must not touch a
        // now-detached generic PlatformView during that same initial pass.
        UpdateSource();
    }

    protected override BrowserElement CreatePlatformView()
    {
        var element = new BrowserElement("fluent-image");
        element.Style("display", "block");
        element.Style("min-width", "0");
        element.Style("min-height", "0");
        element.Style("overflow", "hidden");
        element.Style("line-height", "0");
        return element;
    }

    private static bool HasExplicitSize(IImage view)
    {
        return double.IsFinite(view.Width) && view.Width >= 0 && double.IsFinite(view.Height) && view.Height >= 0;
    }

    private static void MapSize(BrowserImageHandler handler, IImage view)
    {
        handler.UpdateSource();
        handler.Host.RequestLayout();
    }

    private bool IsCurrent(ImageRequest request)
    {
        return ReferenceEquals(_request, request)
            && request.Generation == _generation
            && ReferenceEquals(((IElementHandler)this).PlatformView, request.Element)
            && ReferenceEquals(((IElementHandler)this).VirtualView, request.View)
            && ReferenceEquals(request.View.Source, request.Source)
            && (request.View.Source as IFileImageSource)?.File == request.Filename
            && HasExplicitSize(request.View) == request.HasSize;
    }

    private void UpdateSource()
    {
        if (_settingVirtualView || ((IElementHandler)this).PlatformView is not BrowserElement element)
        {
            return;
        }
        var view = VirtualView;
        var source = view.Source;
        var filename = (source as IFileImageSource)?.File;
        bool hasSize = HasExplicitSize(view);
        var previous = _request;
        if (previous is not null && IsCurrent(previous) && ReferenceEquals(previous.Source, source)
            && previous.Filename == filename && previous.HasSize == hasSize)
        {
            return;
        }

        var request = new ImageRequest(unchecked(++_generation), view, element, source, filename, hasSize);
        _request = request;
        if (previous is not null)
        {
            previous.Pending = false;
        }
        // Invalidate the old JS request before notifying application IsLoading listeners.
        Browser.ImageSource(element.Id, "", request.Generation);
        if (previous is not null && !ReferenceEquals(previous.View, view))
        {
            // MAUI reuses the platform element on SetVirtualView without disconnecting.
            // Retire the old view only after publishing the new request identity: its
            // callback can complete an old load, replace the view/source, or disconnect.
            previous.View.UpdateIsLoading(false);
            if (!IsCurrent(request))
            {
                return;
            }
        }
        string? error = source is null ? null
            : !hasSize ? "Browser Image requires both explicit WidthRequest and HeightRequest."
            : source is not IFileImageSource ? "Browser Image supports only packaged static file sources."
            : string.IsNullOrEmpty(filename) ? "Browser Image requires a nonempty catalog filename."
            : null;
        if (source is null || error is not null)
        {
            view.UpdateIsLoading(false);
            if (error is not null && IsCurrent(request))
            {
                Browser.ReportResource(error);
            }
            return;
        }

        request.Pending = true;
        view.UpdateIsLoading(true);
        // UpdateIsLoading runs app code: it can change Source or detach this handler.
        if (IsCurrent(request) && request.Pending)
        {
            Browser.ImageSource(element.Id, filename!, request.Generation);
        }
    }

    internal void ImageLoaded(int requestId, bool success)
    {
        if (_request is not { Pending: true } request || request.Generation != requestId || !IsCurrent(request))
        {
            return;
        }
        // Settle before application callbacks, including a reentrant duplicate completion.
        // JS reports the resource error; success/failure both end MAUI's loading state.
        request.Pending = false;
        request.View.UpdateIsLoading(false);
    }

    public override void UpdateValue(string property)
    {
        if (((IElementHandler)this).PlatformView is not null)
        {
            base.UpdateValue(property);
        }
    }

    protected override void DisconnectHandler(BrowserElement platformView)
    {
        var request = _request;
        _request = null;
        if (request is not null)
        {
            request.Pending = false;
        }
        try
        {
            // MAUI clears IElementHandler.PlatformView before invoking this hook,
            // so reentrant UpdateValue/UpdateSource cannot start another request.
            request?.View.UpdateIsLoading(false);
        }
        finally
        {
            // The bridge release hook also releases the image adapter's owned img/listeners.
            base.DisconnectHandler(platformView);
        }
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var view = VirtualView;
        if (!HasExplicitSize(view))
        {
            return Size.Zero;
        }
        // Never measure natural image dimensions or bypass MAUI min/max/parent constraints.
        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, view.Width, view.Width, view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, view.Height, view.Height, view.MinimumHeight, view.MaximumHeight));
    }
}