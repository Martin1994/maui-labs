using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

/// <summary>Explicit conversion between MAUI themes and the browser wire contract.</summary>
internal static class BrowserTheme
{
    internal static string FromAppTheme(AppTheme theme)
    {
        // Unspecified must be resolved by the host, not sent as an effective theme.
        return theme switch
        {
            AppTheme.Light => BrowserThemeName.Light,
            AppTheme.Dark => BrowserThemeName.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported effective MAUI theme.")
        };
    }

    internal static AppTheme ToAppTheme(string theme)
    {
        return theme switch
        {
            BrowserThemeName.Light => AppTheme.Light,
            BrowserThemeName.Dark => AppTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported browser theme.")
        };
    }
}