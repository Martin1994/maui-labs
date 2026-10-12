using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

namespace FluentBrowserSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        builder.UseMauiApp<App>()
            .UseFluentUIWebComponents();
        return builder.Build();
    }
}