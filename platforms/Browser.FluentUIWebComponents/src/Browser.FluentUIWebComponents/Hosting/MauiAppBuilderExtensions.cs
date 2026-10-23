using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Registers the supported browser handlers without changing application code or XAML.</summary>
public static class MauiAppBuilderExtensions
{
    public static MauiAppBuilder UseFluentUIWebComponents(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // ConfigureFonts installs factory-backed portable defaults and the standard
        // FontInitializer. Override those defaults without reflecting into its delegates.
        // Apps can replace either interface after UseFluentUIWebComponents as usual.
        builder.ConfigureFonts();
        builder.Services.AddSingleton<IFontRegistrar, BrowserFontRegistrar>();
        builder.Services.AddSingleton<IFontManager, BrowserFontManager>();
        var host = BrowserApplication.Current;
        builder.Services.AddSingleton(host);
        builder.Services.AddSingleton<IDispatcherProvider>(host.Dispatcher);
        builder.Services.AddSingleton<IDispatcher>(host.Dispatcher);
        builder.Services.AddSingleton<ITicker>(host.Ticker);
        builder.Services.AddSingleton<IAnimationManager>(services =>
        {
            return new AnimationManager(services.GetRequiredService<ITicker>());
        });
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Application>(_ =>
            {
                return new ApplicationHandler();
            });
            handlers.AddHandler<Window>(_ =>
            {
                return new WindowHandler();
            });
            handlers.AddHandler<ContentPage>(_ =>
            {
                return new PageHandler();
            });
            handlers.AddHandler<Layout>(_ =>
            {
                return new LayoutHandler();
            });
            handlers.AddHandler<Label>(_ =>
            {
                return new LabelHandler();
            });
            handlers.AddHandler<Image>(_ =>
            {
                return new ImageHandler();
            });
            handlers.AddHandler<Entry>(_ =>
            {
                return new EntryHandler();
            });
            handlers.AddHandler<Button>(_ =>
            {
                return new ButtonHandler();
            });
            handlers.AddHandler<Border>(_ =>
            {
                return new BorderHandler();
            });
            handlers.AddHandler<ScrollView>(_ =>
            {
                return new ScrollViewHandler();
            });
        });
        return builder;
    }
}