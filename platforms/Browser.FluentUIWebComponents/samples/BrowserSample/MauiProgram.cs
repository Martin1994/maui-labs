using FluentBrowserSample.Services;
using FluentBrowserSample.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

namespace FluentBrowserSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder(useDefaults: false);
        builder.UseMauiApp<App>()
            .UseFluentUIWebComponents()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansSample");
            });
        builder.Services.AddSingleton<ICardPresetService, CardPresetService>();
        builder.Services.AddScoped<CardStudioViewModel>();
        builder.Services.AddScoped<MainPage>();
        return builder.Build();
    }
}