using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Main(string[] args)
    {
        // Reflection is confined to the test's host setup, never font initialization.
        using var host = (BrowserApplication)Activator.CreateInstance(typeof(BrowserApplication), nonPublic: true)!;
        typeof(BrowserApplication).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, host);
        if (args is [var scenario] && scenario != "baseline")
        {
            switch (scenario)
            {
                case "reuse":
                    ReuseDuringLoad();
                    break;
                case "reentrant-reuse":
                    ReentrantReuse(disconnect: false);
                    break;
                case "reentrant-reuse-source":
                    ReentrantSourceDuringRetirement();
                    break;
                case "reentrant-reuse-disconnect":
                    ReentrantReuse(disconnect: true);
                    break;
                case "disconnect":
                    ReentrantDisconnect();
                    break;
                case "embedded-fonts":
                    EmbeddedFontInitialization();
                    break;
                default:
                    throw new Exception($"Unknown scenario: {scenario}");
            }
            Console.WriteLine("Managed asset assertions passed");
            return;
        }
        int configurations = 0;
        var builder = MauiApp.CreateBuilder();
        builder.ConfigureFonts(fonts =>
        {
            configurations++;
            fonts.AddFont("body.ttf", "Body");
        });
        builder.UseFluentUIWebComponents();
        builder.ConfigureFonts(fonts =>
        {
            configurations++;
            fonts.AddFont("title.otf", "Title");
        });
        using var app = builder.Build();
        Check(configurations == 2, "Every ConfigureFonts delegate must execute exactly once.");
        var registrar = app.Services.GetRequiredService<IFontRegistrar>();
        Check(registrar is BrowserFontRegistrar, "Portable default registrar was not replaced.");
        Check(registrar.GetFont("Body") == "body.ttf" && registrar.GetFont("body.ttf") == "body.ttf", "Alias/file registration failed.");
        Check(registrar.GetFont("Title") == "title.otf" && registrar.GetFont("BODY") is null, "Ordinal lookup failed.");
        Check(app.Services.GetRequiredService<IFontManager>().DefaultFontSize == 16, "Default size changed.");
        registrar.Register("embedded.ttf", "Embedded", typeof(Program).Assembly);
        Check(registrar.GetFont("embedded.ttf") is null && registrar.GetFont("Embedded") is null,
            "Unsupported embedded fonts must not be registered as packaged files.");
        Check(Browser.Reports[^1].Contains("embedded", StringComparison.OrdinalIgnoreCase), "Embedded font must be diagnosed.");
        Check(BrowserFontManager.ResolveFamily("Body", registrar) == "maui-test-font, system-ui, sans-serif", "Registered font was not resolved.");
        Check(Browser.FontFiles[^1] == "body.ttf", "Alias must not become a catalog URL.");
        Check(BrowserFontManager.ResolveFamily("Arial", registrar) == "Arial, system-ui, sans-serif", "System font fallback failed.");
        Check(BrowserFontManager.ResolveFamily(null, registrar) == "system-ui, sans-serif", "Default family changed.");

        var customRegistrar = new CustomRegistrar();
        var customManager = new CustomManager();
        var customBuilder = MauiApp.CreateBuilder();
        customBuilder.UseFluentUIWebComponents();
        customBuilder.Services.AddSingleton<IFontRegistrar>(customRegistrar);
        customBuilder.Services.AddSingleton<IFontManager>(customManager);
        customBuilder.ConfigureFonts(fonts => fonts.AddFont("custom.ttf", "Custom"));
        using var customApp = customBuilder.Build();
        Check(ReferenceEquals(customApp.Services.GetRequiredService<IFontRegistrar>(), customRegistrar), "Custom registrar replaced.");
        Check(ReferenceEquals(customApp.Services.GetRequiredService<IFontManager>(), customManager), "Custom manager replaced.");
        Check(customRegistrar.Registrations == 1, "Custom registrar must use normal FontInitializer.");
        var label = new Label { Text = "test", FontFamily = "Custom" };
        var labelHandler = new BrowserLabelHandler();
        labelHandler.SetMauiContext(new MauiContext(customApp.Services));
        labelHandler.SetVirtualView(label);
        Check(Browser.FontFiles[^1] == "custom.ttf", "SetFont must support an arbitrary IFontRegistrar/IFontManager.");
        ((IElementHandler)labelHandler).DisconnectHandler();

        var context = new MauiContext(app.Services);
        (Image, BrowserImageHandler) Attach(Image view)
        {
            var handler = new BrowserImageHandler();
            handler.SetMauiContext(context);
            handler.SetVirtualView(view);
            return (view, handler);
        }
        var (image, handler) = Attach(new Image { WidthRequest = 100, HeightRequest = 50, Source = "a.png" });
        var first = Browser.Sources.Last(source => source.Name == "a.png");
        Check(image.IsLoading, "Image must start loading.");
        image.Source = "b.png";
        var second = Browser.Sources[^1];
        image.Source = "a.png";
        var third = Browser.Sources[^1];
        BrowserApplication.OnImageLoaded(first.Id, first.Request, true);
        BrowserApplication.OnImageLoaded(second.Id, second.Request, false);
        Check(image.IsLoading, "Stale completion changed the current image.");
        BrowserApplication.OnImageLoaded(third.Id, third.Request, true);
        Check(!image.IsLoading, "Current completion must stop loading.");
        int notifications = 0;
        image.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading))
            {
                notifications++;
            }
        };
        BrowserApplication.OnImageLoaded(third.Id, third.Request, true);
        Check(notifications == 0, "Duplicate completion must be idempotent.");
        image.Source = null;
        Check(!image.IsLoading && Browser.Sources[^1].Name == "", "Null source must clear.");
        var size = handler.GetDesiredSize(1000, 1000);
        Check(size.Width == 100 && size.Height == 50, "Explicit image size was not respected.");
        size = handler.GetDesiredSize(40, 30);
        Check(size.Width <= 40 && size.Height <= 30, "Parent constraints were bypassed.");
        image.MaximumWidthRequest = 60;
        image.MaximumHeightRequest = 40;
        size = handler.GetDesiredSize(1000, 1000);
        Check(size.Width <= 60 && size.Height <= 40, "MAUI maximum constraints were bypassed.");

        image.WidthRequest = -1;
        image.Source = "needs-size.png";
        Check(!image.IsLoading && Browser.Reports[^1].Contains("WidthRequest"), "Missing explicit size must be diagnosed.");
        Check(handler.GetDesiredSize(100, 100).Width == 0, "Unsupported sizing must not use DOM measurement.");
        image.WidthRequest = 100;
        Check(image.IsLoading && Browser.Sources[^1].Name == "needs-size.png", "Adding explicit size must retry.");
        image.Source = new UriImageSource { Uri = new Uri("https://example.test/image.png") };
        Check(!image.IsLoading && Browser.Reports[^1].Contains("static file"), "URI source must be rejected.");
        image.Source = "before.png";
        handler.ImageLoaded(Browser.Sources[^1].Request, true);

        bool redirect = true;
        image.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && image.IsLoading && redirect)
            {
                redirect = false;
                image.Source = "redirect.png";
            }
        };
        int start = Browser.Sources.Count;
        image.Source = "outer.png";
        // BindableObject can queue a same-property reentrant write until the outer
        // Source setter finishes. An outer load may begin, but cannot win afterward.
        Check(Browser.Sources[^1].Name == "redirect.png", "Reentrant source update was overwritten.");
        foreach (var stale in Browser.Sources.Skip(start).Where(source => source.Name == "outer.png"))
        {
            handler.ImageLoaded(stale.Request, true);
            Check(image.IsLoading, "An outer request completed the redirected image.");
        }
        handler.ImageLoaded(Browser.Sources[^1].Request, true);

        image.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && image.IsLoading)
            {
                ((IElementHandler)handler).DisconnectHandler();
            }
        };
        start = Browser.Sources.Count;
        image.Source = "detach.png";
        Check(!image.IsLoading && ((IElementHandler)handler).PlatformView is null, "Reentrant disconnect did not clear loading.");
        Check(Browser.Sources.Skip(start).All(source => source.Name == ""), "Detached handler initiated a new load.");
        handler.UpdateValue(nameof(Image.Source));
        BrowserApplication.OnImageLoaded(third.Id, third.Request, false);
        Check(!image.IsLoading, "Late completion revived a detached handler.");

        var initialDetach = new Image { WidthRequest = 50, HeightRequest = 50, Source = "initial-detach.png" };
        initialDetach.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && initialDetach.IsLoading)
            {
                initialDetach.Handler!.DisconnectHandler();
            }
        };
        var (_, initiallyDetachedHandler) = Attach(initialDetach);
        Check(!initialDetach.IsLoading && ((IElementHandler)initiallyDetachedHandler).PlatformView is null,
            "Initial loading notification must permit disconnection without breaking later mapper entries.");
        Console.WriteLine("Managed asset assertions passed");
    }

    private static Image NewImage(string filename) => new() { WidthRequest = 100, HeightRequest = 50, Source = filename };

    private static BrowserImageHandler Attach(Image image, MauiApp app)
    {
        var handler = new BrowserImageHandler();
        handler.SetMauiContext(new MauiContext(app.Services));
        handler.SetVirtualView(image);
        return handler;
    }

    private static void ReuseDuringLoad()
    {
        using var app = MauiApp.CreateBuilder().UseFluentUIWebComponents().Build();
        var first = NewImage("same.png");
        var handler = Attach(first, app);
        var oldRequest = Browser.Sources[^1];
        var element = ((IElementHandler)handler).PlatformView;
        int settled = 0;
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && !first.IsLoading)
            {
                settled++;
            }
        };
        // Identical source identity must still represent a different view/request.
        var second = NewImage("unused.png");
        second.Source = first.Source;
        handler.SetVirtualView(second);
        Check(ReferenceEquals(element, ((IElementHandler)handler).PlatformView), "MAUI must reuse the platform element.");
        Check(!first.IsLoading && settled == 1, "Replacing a virtual view must settle the old pending image exactly once.");
        Check(second.IsLoading, "Replacement image must start loading.");
        var replacement = Browser.Sources[^1];
        Check(replacement.Id == oldRequest.Id && replacement.Request != oldRequest.Request, "Reuse must advance request generation.");
        BrowserApplication.OnImageLoaded(oldRequest.Id, oldRequest.Request, true);
        BrowserApplication.OnImageLoaded(oldRequest.Id, oldRequest.Request, false);
        Check(second.IsLoading && settled == 1, "Old completion must not settle either view again.");
        handler.SetVirtualView(second);
        Check(Browser.Sources[^1] == replacement, "Setting the same virtual view must not restart its request.");
        BrowserApplication.OnImageLoaded(replacement.Id, replacement.Request, true);
        Check(!second.IsLoading, "Replacement completion must settle the current view.");
        ((IElementHandler)handler).DisconnectHandler();
    }

    private static void ReentrantReuse(bool disconnect)
    {
        using var app = MauiApp.CreateBuilder().UseFluentUIWebComponents().Build();
        var first = NewImage("old.png");
        var second = NewImage("superseded.png");
        var third = NewImage("winner.png");
        var handler = Attach(first, app);
        var oldRequest = Browser.Sources[^1];
        int callbacks = 0;
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(Image.IsLoading) || first.IsLoading)
            {
                return;
            }
            callbacks++;
            // The old generation must already be retired before invoking app code.
            BrowserApplication.OnImageLoaded(oldRequest.Id, oldRequest.Request, true);
            handler.UpdateValue(nameof(Image.Source));
            if (disconnect)
            {
                ((IElementHandler)handler).DisconnectHandler();
            }
            else
            {
                handler.SetVirtualView(third);
            }
        };
        handler.SetVirtualView(second);
        Check(callbacks == 1 && !first.IsLoading, "Retirement must notify the old view once, even with reentrant completion.");
        Check(!second.IsLoading, "Reentrant replacement/disconnect must not leave the superseded view loading.");
        Check(Browser.Sources.All(source => source.Name != "superseded.png"), "Outer replacement started after being superseded.");
        if (disconnect)
        {
            Check(((IElementHandler)handler).PlatformView is null, "Retirement callback must permit disconnect.");
        }
        else
        {
            Check(ReferenceEquals(((IElementHandler)handler).VirtualView, third) && third.IsLoading,
                "Reentrant virtual view replacement must win.");
            Check(Browser.Sources[^1].Name == "winner.png", "Outer replacement overwrote the reentrant request.");
            foreach (var stale in Browser.Sources.Where(source => source.Request != Browser.Sources[^1].Request).ToArray())
            {
                BrowserApplication.OnImageLoaded(stale.Id, stale.Request, false);
                Check(third.IsLoading, "Retired completion settled the reentrant request.");
            }
            var current = Browser.Sources[^1];
            BrowserApplication.OnImageLoaded(current.Id, current.Request, true);
            Check(!third.IsLoading, "Reentrant replacement must remain completable.");
            ((IElementHandler)handler).DisconnectHandler();
        }
    }

    private static void ReentrantSourceDuringRetirement()
    {
        using var app = MauiApp.CreateBuilder().UseFluentUIWebComponents().Build();
        var first = NewImage("old.png");
        var second = NewImage("superseded.png");
        var handler = Attach(first, app);
        var oldRequest = Browser.Sources[^1];
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && !first.IsLoading)
            {
                Check(Browser.Sources[^1].Name == "" && Browser.Sources[^1].Request != oldRequest.Request,
                    "JS generation must be invalidated before the old loading callback.");
                second.Source = "redirected.png";
            }
        };
        handler.SetVirtualView(second);
        Check(!first.IsLoading && second.IsLoading, "Reentrant source change must transfer loading to the new view.");
        Check(Browser.Sources[^1].Name == "redirected.png"
            && Browser.Sources.All(source => source.Name != "superseded.png"), "Retirement callback's source change was overwritten.");
        var current = Browser.Sources[^1];
        BrowserApplication.OnImageLoaded(oldRequest.Id, oldRequest.Request, true);
        Check(second.IsLoading, "Old completion settled the redirected replacement.");
        BrowserApplication.OnImageLoaded(current.Id, current.Request, true);
        Check(!second.IsLoading, "Redirected replacement could not complete.");
        ((IElementHandler)handler).DisconnectHandler();
    }

    private static void ReentrantDisconnect()
    {
        using var app = MauiApp.CreateBuilder().UseFluentUIWebComponents().Build();
        var image = NewImage("pending.png");
        var handler = Attach(image, app);
        var oldRequest = Browser.Sources[^1];
        int callbacks = 0;
        image.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Image.IsLoading) && !image.IsLoading)
            {
                callbacks++;
                Check(((IElementHandler)handler).PlatformView is null, "MAUI must clear PlatformView before the disconnect callback.");
                handler.UpdateValue(nameof(Image.Source));
                image.Source = "during-disconnect.png";
            }
        };
        int start = Browser.Sources.Count;
        ((IElementHandler)handler).DisconnectHandler();
        Check(callbacks == 1 && !image.IsLoading, "Reentrant source mapping during disconnect left IsLoading stuck.");
        Check(Browser.Sources.Skip(start).All(source => source.Name == ""), "Disconnect callback must not start a new load.");
        Check(((IElementHandler)handler).PlatformView is null, "Disconnect must release the platform element.");
        BrowserApplication.OnImageLoaded(oldRequest.Id, oldRequest.Request, true);
        Check(!image.IsLoading, "Late completion revived a disconnected image.");
        // A lifecycle guard must not permanently disable this reusable handler.
        var next = NewImage("reconnected.png");
        handler.SetVirtualView(next);
        Check(next.IsLoading && Browser.Sources[^1].Name == "reconnected.png", "Handler could not reconnect after disconnect.");
        var current = Browser.Sources[^1];
        BrowserApplication.OnImageLoaded(current.Id, current.Request, true);
        Check(!next.IsLoading, "Reconnected image could not complete.");
        ((IElementHandler)handler).DisconnectHandler();
    }

    private static void EmbeddedFontInitialization()
    {
        int configurations = 0;
        var builder = MauiApp.CreateBuilder().UseFluentUIWebComponents();
        builder.ConfigureFonts(fonts =>
        {
            configurations++;
            fonts.AddEmbeddedResourceFont(typeof(Program).Assembly, "embedded.ttf", "Embedded");
            fonts.AddEmbeddedResourceFont(typeof(Program).Assembly, "without-alias.otf");
            fonts.AddFont("packaged.ttf", "Packaged");
        });
        // Exercise the real FontInitializer inside Build, not a direct registrar-only call.
        using var app = builder.Build();
        Check(configurations == 1, "Embedded font ConfigureFonts delegate must execute normally once.");
        var registrar = app.Services.GetRequiredService<IFontRegistrar>();
        Check(registrar is BrowserFontRegistrar, "Build must use the browser registrar.");
        Check(Browser.FatalReports.Count == 0, "Unsupported font must never use the fatal host diagnostic.");
        Check(Browser.Reports.Count == 2 && Browser.Reports.All(report => report.Contains("MauiFont") && report.Contains("AddFont")),
            "Every unsupported registration needs an actionable nonfatal resource diagnostic.");
        foreach (var family in new[] { "Embedded", "embedded.ttf", "without-alias.otf" })
        {
            Check(registrar.GetFont(family) is null, "Embedded resource must not be mistaken for a catalog font.");
            Check(BrowserFontManager.ResolveFamily(family, registrar) == family + ", system-ui, sans-serif",
                "Unsupported embedded font must retain normal CSS fallback.");
        }
        Check(Browser.FontFiles.Count == 0, "Embedded filename or alias must never reach the catalog font loader.");
        Check(BrowserFontManager.ResolveFamily("Packaged", registrar) == "maui-test-font, system-ui, sans-serif"
            && Browser.FontFiles.SequenceEqual(new[] { "packaged.ttf" }), "Unsupported registration broke subsequent AddFont initialization.");
    }

    private sealed class CustomManager : IFontManager
    {
        public double DefaultFontSize => 19;
    }

    private sealed class CustomRegistrar : IFontRegistrar
    {
        public int Registrations { get; private set; }
        public string? GetFont(string font) => font == "Custom" ? "custom.ttf" : null;

        public void Register(string filename, string? alias)
        {
            Registrations++;
        }

        public void Register(string filename, string? alias, Assembly assembly)
        {
            throw new NotSupportedException();
        }
    }
}