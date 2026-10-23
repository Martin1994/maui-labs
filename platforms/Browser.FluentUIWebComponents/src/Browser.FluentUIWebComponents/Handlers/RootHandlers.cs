using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Connects IApplication to the browser host without creating a DOM control.</summary>
/// <remarks>Maps the effective MAUI theme to Fluent; platform-theme discovery belongs to the host.</remarks>
internal sealed class ApplicationHandler : ElementHandler<IApplication, BrowserApplication>
{
    protected override BrowserApplication CreatePlatformElement()
    {
        return MauiContext!.Services.GetRequiredService<BrowserApplication>();
    }

    private static readonly IPropertyMapper<IApplication, ApplicationHandler> Properties = new PropertyMapper<IApplication, ApplicationHandler>
    {
        [nameof(IApplication.UserAppTheme)] = (h, v) =>
        {
            h.PlatformView.ApplyTheme(v.UserAppTheme);
        },
    };

    public ApplicationHandler() : base(Properties)
    {
    }
}

/// <summary>Hosts the single MAUI window in a standard root <c>div</c>.</summary>
/// <remarks>Mounting owns viewport observation; content replacement and shutdown disconnect child handlers.</remarks>
internal sealed class WindowHandler : ElementHandler<IWindow, BrowserElement>
{
    protected override BrowserElement CreatePlatformElement()
    {
        return new("div");
    }

    private IView? _content;
    private BrowserApplication Host
    {
        get
        {
            return MauiContext!.Services.GetRequiredService<BrowserApplication>();
        }
    }
    private static readonly IPropertyMapper<IWindow, WindowHandler> Properties = new PropertyMapper<IWindow, WindowHandler>
    {
        [nameof(IWindow.Content)] = (h, _) =>
        {
            h.SetContent();
        },
    };
    public WindowHandler() : base(Properties)
    {
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