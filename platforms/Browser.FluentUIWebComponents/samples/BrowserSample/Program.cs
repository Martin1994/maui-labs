using System.Runtime.Versioning;
using Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

[assembly: SupportedOSPlatform("browser")]

await FluentBrowserSample.SampleDiagnostics.InitializeAsync();
return BrowserApplication.Run(FluentBrowserSample.MauiProgram.CreateMauiApp);