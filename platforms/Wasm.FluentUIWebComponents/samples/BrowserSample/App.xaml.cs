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
        var window = new Window(new MainPage());
        window.Destroying += (_, _) =>
        {
            SampleDiagnostics.Dispose();
        };
        return window;
    }
}