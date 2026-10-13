using System.Runtime.Versioning;
using Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

[assembly: SupportedOSPlatform("browser")]

await FluentBrowserSample.SampleDiagnostics.InitializeAsync();
return BrowserApplication.Run(FluentBrowserSample.MauiProgram.CreateMauiApp);