using System.Runtime.InteropServices.JavaScript;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal static partial class Browser
{
    [JSImport("imageSource", "maui-browser")]
    internal static partial void ImageSource(int id, string name, int request);

    [JSImport("imageAspect", "maui-browser")]
    internal static partial void ImageAspect(int id, string aspect);

    [JSImport("fontFamily", "maui-browser")]
    internal static partial string FontFamily(string filename);

    [JSImport("reportResource", "maui-browser")]
    internal static partial void ReportResource(string message);
}