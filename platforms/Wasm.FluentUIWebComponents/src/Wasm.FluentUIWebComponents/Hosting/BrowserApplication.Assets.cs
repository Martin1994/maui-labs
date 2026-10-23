using System.Runtime.InteropServices.JavaScript;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

public sealed partial class BrowserApplication
{
    [JSExport]
    public static void OnImageLoaded(int id, int request, bool success)
    {
        try
        {
            if (_current is { _closed: false } host && host._handlers.TryGetValue(id, out var handler)
                && handler is BrowserImageHandler image)
            {
                image.ImageLoaded(request, success);
            }
        }
        catch (Exception exception)
        {
            // A resource callback must not transition the whole host to its fatal state.
            Browser.ReportResource(exception.ToString());
        }
    }
}