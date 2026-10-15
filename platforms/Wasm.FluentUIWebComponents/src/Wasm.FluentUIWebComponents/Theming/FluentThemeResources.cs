using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

/// <summary>
/// A small set of MAUI Color resources sourced from the active Fluent light/dark
/// theme. Merge into application resources and consume using DynamicResource.
/// </summary>
public sealed class FluentThemeResources : ResourceDictionary
{
    public const string BrandForegroundColorKey = "Fluent.BrandForegroundColor";
    public const string ForegroundColorKey = "Fluent.ForegroundColor";
    public const string MutedColorKey = "Fluent.MutedColor";
    public const string CanvasColorKey = "Fluent.CanvasColor";
    public const string SurfaceColorKey = "Fluent.SurfaceColor";
    public const string StrokeColorKey = "Fluent.StrokeColor";

    public FluentThemeResources()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Create FluentThemeResources after the MAUI Application has been constructed.");
        UpdateColors(application.RequestedTheme);
        // MAUI implements this event with WeakEventManager. A removed dictionary
        // is not kept alive by the application; no static dictionary is retained.
        application.RequestedThemeChanged += OnRequestedThemeChanged;
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        UpdateColors(e.RequestedTheme);
    }

    private void UpdateColors(AppTheme theme)
    {
        string themeName = BrowserTheme.FromAppTheme(theme);
        this[BrandForegroundColorKey] = ReadColor("colorBrandForeground1", themeName);
        this[ForegroundColorKey] = ReadColor("colorNeutralForeground1", themeName);
        this[MutedColorKey] = ReadColor("colorNeutralForeground3", themeName);
        this[CanvasColorKey] = ReadColor("colorNeutralBackground3", themeName);
        this[SurfaceColorKey] = ReadColor("colorNeutralBackground1", themeName);
        this[StrokeColorKey] = ReadColor("colorNeutralStroke2", themeName);
    }

    private static Color ReadColor(string token, string theme)
    {
        // These preset Fluent color tokens are hex colors, not CSS var() values.
        return Color.FromArgb(Browser.GetThemeColor(token, theme));
    }
}