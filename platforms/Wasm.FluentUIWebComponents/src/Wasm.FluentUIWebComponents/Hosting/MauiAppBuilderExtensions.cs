using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Registers the supported browser handlers without changing application code or XAML.</summary>
public static class MauiAppBuilderExtensions
{
    public static MauiAppBuilder UseFluentUIWebComponents(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
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
                return new BrowserApplicationHandler();
            });
            handlers.AddHandler<Window>(_ =>
            {
                return new BrowserWindowHandler();
            });
            handlers.AddHandler<ContentPage>(_ =>
            {
                return new BrowserPageHandler();
            });
            handlers.AddHandler<Layout>(_ =>
            {
                return new BrowserLayoutHandler();
            });
            handlers.AddHandler<Label>(_ =>
            {
                return new BrowserLabelHandler();
            });
            handlers.AddHandler<Entry>(_ =>
            {
                return new BrowserEntryHandler();
            });
            handlers.AddHandler<Button>(_ =>
            {
                return new BrowserButtonHandler();
            });
            handlers.AddHandler<Border>(_ =>
            {
                return new BrowserBorderHandler();
            });
            handlers.AddHandler<ScrollView>(_ =>
            {
                return new BrowserScrollViewHandler();
            });
        });
        return builder;
    }
}