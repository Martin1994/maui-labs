using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;

[assembly: SupportedOSPlatform("browser")]

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Single-window browser platform host. The application is supplied by the consumer.</summary>
public sealed partial class BrowserApplication : IPlatformApplication, IDisposable
{
    private static BrowserApplication? _current;
    private MauiApp? _mauiApp;
    private IServiceScope? _scope;
    private IApplication? _application;
    private BrowserAppInfo? _appInfo;
    private IWindow? _window;
    private IElementHandler? _applicationHandler;
    private IElementHandler? _windowHandler;
    private readonly Dictionary<int, IElementHandler> _handlers = new();
    private bool _closed;
    private bool _insideLayout;
    private bool _created;
    private bool _activated;

    private BrowserApplication()
    {
    }
    internal static BrowserApplication Current
    {
        get
        {
            return _current ?? throw new InvalidOperationException("Call BrowserApplication.Run before creating the MauiApp.");
        }
    }
    internal BrowserDispatcher Dispatcher
    {
        get;
    } = new();
    internal Dictionary<int, Action> Clicks
    {
        get;
    } = new();
    internal Dictionary<int, Action<double, double>> Scrolls
    {
        get;
    } = new();
    internal bool WindowAttached
    {
        get;
        set;
    }
    public IServiceProvider Services
    {
        get
        {
            return _mauiApp?.Services ?? throw new InvalidOperationException("MauiApp has not been built.");
        }
    }
    public IApplication Application
    {
        get
        {
            return _application ?? throw new InvalidOperationException("Application has not been created.");
        }
    }

    public static int Run(Func<MauiApp> createApp)
    {
        ArgumentNullException.ThrowIfNull(createApp);
        if (_current is not null)
        {
            throw new InvalidOperationException("Only one browser application may run at a time.");
        }
        var host = new BrowserApplication();
        _current = host;
        IPlatformApplication.Current = host;
        DispatcherProvider.SetCurrent(host.Dispatcher);
        try
        {
            // Must precede Application construction and initial XAML theme bindings.
            host._appInfo = BrowserAppInfo.Install(BrowserTheme.ToAppTheme(Browser.GetSystemTheme()));
            host._mauiApp = createApp();
            host._application = host.Services.GetRequiredService<IApplication>();
            host._applicationHandler = Attach(host.Application, new MauiContext(host.Services));
            host._scope = host.Services.CreateScope();
            var context = new MauiContext(host._scope.ServiceProvider);
            host._window = host.Application.CreateWindow(new ActivationState(context));
            host._windowHandler = Attach(host._window, context);
            host._created = true;
            host._window.Created();
            host._activated = true;
            host._window.Activated();
            host.UpdateSystemTheme(Browser.StartThemeTracking());
            host.RequestLayout();
            return 0;
        }
        catch (Exception exception)
        {
            Browser.Report(exception.ToString());
            host.Dispose();
            return 1;
        }
    }

    internal void ApplyTheme(AppTheme userTheme)
    {
        if (_closed)
        {
            return;
        }
        var effectiveTheme = userTheme == AppTheme.Unspecified ? _appInfo!.RequestedTheme : userTheme;
        Browser.ApplyTheme(BrowserTheme.FromAppTheme(effectiveTheme));
    }

    private void UpdateSystemTheme(string theme)
    {
        if (_closed || _appInfo is null || _application is null)
        {
            return;
        }
        _appInfo.RequestedTheme = BrowserTheme.ToAppTheme(theme);
        // MAUI updates PlatformAppTheme even under an explicit user override,
        // then notifies bindings/handler only if the effective theme changes.
        _application.ThemeChanged();
    }

    private static IElementHandler Attach(IElement element, IMauiContext context)
    {
        if (element.Handler is not null)
        {
            return element.Handler;
        }
        var handler = context.Handlers.GetHandler(element.GetType())
            ?? throw new NotSupportedException($"No browser handler for {element.GetType().FullName}.");
        handler.SetMauiContext(context);
        handler.SetVirtualView(element);
        return handler;
    }

    internal BrowserElement Realize(IView view, IMauiContext context)
    {
        return Attach(view, context).PlatformView as BrowserElement ?? throw new NotSupportedException($"{view.GetType().Name} has no browser platform view.");
    }
    internal void Track(BrowserElement element, IElementHandler handler)
    {
        _handlers.Add(element.Id, handler);
    }
    internal void Untrack(BrowserElement element)
    {
        _handlers.Remove(element.Id);
    }
    internal void Disconnect(IElement element)
    {
        if (element.Handler is { PlatformView: not null } handler)
        {
            handler.DisconnectHandler();
        }
    }
    internal void RequestLayout()
    {
        if (!_closed && WindowAttached)
        {
            Browser.RequestLayout();
        }
    }
    private void Layout(double width, double height)
    {
        if (_closed || !WindowAttached || _insideLayout || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }
        _insideLayout = true;
        try
        {
            _window!.FrameChanged(new Rect(0, 0, width, height));
            ((BrowserElement)_windowHandler!.PlatformView!).Frame(new Rect(0, 0, width, height));
            var root = _window.Content;
            root?.Measure(width, height);
            root?.Arrange(new Rect(0, 0, width, height));
        }
        finally
        {
            _insideLayout = false;
        }
    }
    private static void Boundary(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Browser.Report(exception.ToString());
        }
    }
    [JSExport]
    public static void LayoutTick(double width, double height)
    {
        Boundary(() =>
        {
            _current?.Layout(width, height);
        });
    }
    [JSExport]
    public static void OnClick(int id)
    {
        Boundary(() =>
        {
            if (_current is { _closed: false } host && host.Clicks.TryGetValue(id, out var callback))
            {
                callback();
            }
        });
    }
    [JSExport]
    public static void RunScheduled(int id)
    {
        Boundary(() =>
        {
            _current?.Dispatcher.Run(id);
        });
    }
    [JSExport]
    public static void OnScroll(int id, double x, double y)
    {
        Boundary(() =>
        {
            if (_current is { _closed: false } host && host.Scrolls.TryGetValue(id, out var callback))
            {
                callback(x, y);
            }
        });
    }
    [JSExport]
    public static void OnSystemThemeChanged(string theme)
    {
        Boundary(() =>
        {
            _current?.UpdateSystemTheme(theme);
        });
    }
    [JSExport]
    public static void Shutdown()
    {
        Boundary(() =>
        {
            _current?.Dispose();
        });
    }

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        Boundary(Browser.Stop);
        Boundary(Dispatcher.Dispose);
        if (_activated)
        {
            Boundary(() =>
            {
                _window!.Deactivated();
            });
        }
        if (_created)
        {
            Boundary(() =>
            {
                _window!.Destroying();
            });
        }
        foreach (var handler in _handlers.Values.Reverse().ToArray())
        {
            if (handler.PlatformView is not null)
            {
                Boundary(handler.DisconnectHandler);
            }
        }
        if (_applicationHandler?.PlatformView is not null)
        {
            Boundary(_applicationHandler.DisconnectHandler);
        }
        Clicks.Clear();
        Scrolls.Clear();
        _handlers.Clear();
        if (_scope is not null)
        {
            Boundary(_scope.Dispose);
        }
        if (_mauiApp is not null)
        {
            Boundary(_mauiApp.Dispose);
        }
        if (_appInfo is not null)
        {
            Boundary(_appInfo.Dispose);
            _appInfo = null;
        }
        if (ReferenceEquals(IPlatformApplication.Current, this))
        {
            IPlatformApplication.Current = null;
        }
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }
}