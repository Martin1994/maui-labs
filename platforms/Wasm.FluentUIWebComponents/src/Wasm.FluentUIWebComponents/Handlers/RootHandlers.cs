using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserApplicationHandler : ElementHandler<IApplication, BrowserApplication>
{
    public BrowserApplicationHandler() : base(new PropertyMapper<IApplication, BrowserApplicationHandler>())
    {
    }
    protected override BrowserApplication CreatePlatformElement()
    {
        return MauiContext!.Services.GetRequiredService<BrowserApplication>();
    }
}

internal sealed class BrowserWindowHandler : ElementHandler<IWindow, BrowserElement>
{
    private IView? _content;
    private BrowserApplication Host
    {
        get
        {
            return MauiContext!.Services.GetRequiredService<BrowserApplication>();
        }
    }
    private static readonly IPropertyMapper<IWindow, BrowserWindowHandler> Properties = new PropertyMapper<IWindow, BrowserWindowHandler>
    {
        [nameof(IWindow.Content)] = (h, _) =>
        {
            h.SetContent();
        },
    };
    public BrowserWindowHandler() : base(Properties)
    {
    }
    protected override BrowserElement CreatePlatformElement()
    {
        return new("div");
    }
    protected override void ConnectHandler(BrowserElement platformView)
    {
        base.ConnectHandler(platformView);
        Host.Track(platformView, this);
        Browser.Mount(platformView.Id);
        Host.WindowAttached = true;
    }
    private void SetContent()
    {
        if (ReferenceEquals(_content, VirtualView.Content))
        {
            return;
        }
        if (_content is not null)
        {
            Host.Disconnect(_content);
        }
        _content = VirtualView.Content;
        if (_content is not null)
        {
            Browser.Insert(PlatformView.Id, Host.Realize(_content, MauiContext!).Id, 0);
        }
        Host.RequestLayout();
    }
    protected override void DisconnectHandler(BrowserElement platformView)
    {
        Host.WindowAttached = false;
        Browser.Unmount(platformView.Id);
        if (_content is not null)
        {
            Host.Disconnect(_content);
        }
        _content = null;
        Host.Untrack(platformView);
        Browser.Release(platformView.Id);
        base.DisconnectHandler(platformView);
    }
}