using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolve from the window scope after App resources have been initialized.
        // Constructor-injecting MainPage into App would create the page too early.
        var services = activationState?.Context.Services
            ?? throw new InvalidOperationException("A window service scope is required.");
        var window = new Window(services.GetRequiredService<MainPage>());
        window.Destroying += (_, _) =>
        {
            SampleDiagnostics.Dispose();
        };
        return window;
    }
}