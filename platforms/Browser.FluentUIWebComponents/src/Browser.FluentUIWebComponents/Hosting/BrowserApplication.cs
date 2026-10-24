using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;

[assembly: SupportedOSPlatform("browser")]

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

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
    private IDispatcherProvider? _previousDispatcherProvider;
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
    internal BrowserTicker Ticker
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
        host._previousDispatcherProvider = DispatcherProvider.Current;
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
            try
            {
                Browser.Report(exception.ToString());
            }
            finally
            {
                host.Dispose();
            }
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
            if (ReferenceEquals(element.Handler, handler))
            {
                element.Handler = null;
            }
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
    private static void CleanupBoundary(Action action)
    {
        try
        {
            Boundary(action);
        }
        catch (Exception)
        {
            // Reporting can fail when the JS bridge is shutting down. Continue
            // releasing resources and restoring globals even in that case.
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
    public static void OnPickerSelection(int id, int index)
    {
        WithPicker(id, picker => picker.Selected(index));
    }
    [JSExport]
    public static void OnPickerOpen(int id, bool open)
    {
        WithPicker(id, picker => picker.Opened(open));
    }
    [JSExport]
    public static void OnPickerFocus(int id, bool focused)
    {
        WithPicker(id, picker => picker.Focused(focused));
    }
    private static void WithPicker(int id, Action<PickerHandler> action)
    {
        Boundary(() =>
        {
            if (_current is { _closed: false } host && host._handlers.TryGetValue(id, out var handler)
                && handler is PickerHandler picker && handler.PlatformView is not null)
            {
                action(picker);
            }
        });
    }
    [JSExport]
    public static void OnSwitchChanged(int id, bool isOn)
    {
        WithSwitch(id, control => control.Changed(isOn));
    }
    [JSExport]
    public static void OnSwitchFocus(int id, bool focused)
    {
        WithSwitch(id, control => control.Focused(focused));
    }
    private static void WithSwitch(int id, Action<SwitchHandler> action)
    {
        Boundary(() =>
        {
            if (_current is { _closed: false } host && host._handlers.TryGetValue(id, out var handler)
                && handler is SwitchHandler control && handler.PlatformView is not null)
            {
                action(control);
            }
        });
    }
    private static void WithInput(int id, Action<EntryHandler> action)
    {
        Boundary(() =>
        {
            if (_current is { _closed: false } host && host._handlers.TryGetValue(id, out var handler)
                && handler is EntryHandler entry)
            {
                action(entry);
            }
        });
    }
    [JSExport]
    public static void OnInput(int id, string value, int start, int length)
    {
        WithInput(id, entry =>
        {
            entry.Input(value, start, length);
        });
    }
    [JSExport]
    public static void OnInputSelection(int id, int start, int length)
    {
        WithInput(id, entry =>
        {
            entry.Selection(start, length);
        });
    }
    [JSExport]
    public static void OnInputFocus(int id, bool focused)
    {
        WithInput(id, entry =>
        {
            entry.Focused(focused);
        });
    }
    [JSExport]
    public static void OnInputCompleted(int id)
    {
        WithInput(id, entry =>
        {
            entry.Completed();
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
    public static void AnimationTick()
    {
        Boundary(() =>
        {
            _current?.Ticker.Tick();
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
    public static void OnSystemThemeChanged([JSStringUnion(typeof(BrowserThemeName))] string theme)
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
        CleanupBoundary(Browser.Stop);
        CleanupBoundary(Dispatcher.Dispose);
        CleanupBoundary(Ticker.Dispose);
        if (_activated)
        {
            CleanupBoundary(() =>
            {
                _window!.Deactivated();
            });
        }
        if (_created)
        {
            CleanupBoundary(() =>
            {
                _window!.Destroying();
            });
        }
        foreach (var handler in _handlers.Values.Reverse().ToArray())
        {
            CleanupBoundary(() =>
            {
                if (handler.PlatformView is not null)
                {
                    handler.DisconnectHandler();
                }
            });
        }
        CleanupBoundary(() =>
        {
            if (_applicationHandler?.PlatformView is not null)
            {
                _applicationHandler.DisconnectHandler();
            }
        });
        Clicks.Clear();
        Scrolls.Clear();
        _handlers.Clear();
        if (_scope is not null)
        {
            CleanupBoundary(_scope.Dispose);
        }
        if (_mauiApp is not null)
        {
            CleanupBoundary(_mauiApp.Dispose);
        }
        if (_appInfo is not null)
        {
            CleanupBoundary(_appInfo.Dispose);
            _appInfo = null;
        }
        if (_previousDispatcherProvider is not null && ReferenceEquals(DispatcherProvider.Current, Dispatcher))
        {
            DispatcherProvider.SetCurrent(_previousDispatcherProvider);
        }
        _previousDispatcherProvider = null;
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