using System.Runtime.Versioning;
using Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

[assembly: SupportedOSPlatform("browser")]

return BrowserApplication.Run(FluentBrowserSample.MauiProgram.CreateMauiApp);