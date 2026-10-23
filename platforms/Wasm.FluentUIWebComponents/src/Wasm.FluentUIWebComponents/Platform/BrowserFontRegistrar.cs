using System.Reflection;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>Maps standard MAUI font registrations to catalog filenames, never deployment URLs.</summary>
internal sealed class BrowserFontRegistrar : IFontRegistrar
{
    private readonly Dictionary<string, string> _fonts = new(StringComparer.Ordinal);

    public void Register(string filename, string? alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        _fonts[filename] = filename;
        if (!string.IsNullOrWhiteSpace(alias))
        {
            _fonts[alias] = filename;
        }
    }

    public void Register(string filename, string? alias, Assembly assembly)
    {
        // FontInitializer runs inside MauiAppBuilder.Build after creating its provider.
        // A known unsupported resource must not throw there and strand that provider.
        // Do not register either name: embedded filenames are not packaged catalog keys.
        Browser.ReportResource($"Browser font '{filename}' does not support embedded assembly resources. Use MauiFont and AddFont(filename, alias); the fallback font will be used.");
    }

    public string? GetFont(string font)
    {
        return _fonts.TryGetValue(font, out var filename) ? filename : null;
    }
}