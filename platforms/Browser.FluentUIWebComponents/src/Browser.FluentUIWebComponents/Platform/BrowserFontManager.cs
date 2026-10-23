namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>The portable MAUI font manager contract only exposes the default size.</summary>
internal sealed class BrowserFontManager : IFontManager
{
    internal const string FallbackFamily = "system-ui, sans-serif";

    public double DefaultFontSize => 16;

    internal static string ResolveFamily(string? family, IFontRegistrar registrar)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return FallbackFamily;
        }
        try
        {
            // Use the standard registrar interface, including application replacements.
            // Unknown names remain system CSS families; registered names are catalog keys.
            var filename = registrar.GetFont(family);
            return filename is null ? family + ", " + FallbackFamily
                : Browser.FontFamily(filename) + ", " + FallbackFamily;
        }
        catch (Exception exception)
        {
            Browser.ReportResource($"Unable to resolve font '{family}': {exception.Message}");
            return FallbackFamily;
        }
    }
}