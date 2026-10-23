using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

// Only theme is implemented here. Other Essentials members retain the previous
// provider's behavior; this is not a general browser Essentials implementation.
internal sealed class BrowserAppInfo : IAppInfo, IDisposable
{
    private readonly IAppInfo _previous;

    private BrowserAppInfo(IAppInfo previous, AppTheme systemTheme)
    {
        _previous = previous;
        RequestedTheme = systemTheme;
    }

    /// <summary>
    /// Installs the browser provider before the MAUI application is constructed.
    /// First-party platforms compile their AppInfo implementation into Essentials,
    /// where AppInfo.Current creates it lazily. External backends (including Labs)
    /// must replace that default; MAUI 10.0.41 exposes no public registration API,
    /// so this backend invokes the internal AppInfo.SetCurrent via reflection.
    /// </summary>
    internal static BrowserAppInfo Install(AppTheme systemTheme)
    {
        var provider = new BrowserAppInfo(AppInfo.Current, systemTheme);
        SetCurrent(provider);
        return provider;
    }

    // MAUI 10.0.41 reads AppInfo statically in Application's constructor and
    // IApplication.ThemeChanged. DI registration alone cannot replace it.
    // Preserve only this internal entry point; verify it on MAUI upgrades.
    [DynamicDependency("SetCurrent", typeof(AppInfo))]
    private static void SetCurrent(IAppInfo provider)
    {
        var method = typeof(AppInfo).GetMethod("SetCurrent", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(AppInfo).FullName, "SetCurrent");
        method.Invoke(null, new object[] { provider });
    }

    public AppTheme RequestedTheme
    {
        get;
        internal set;
    }

    public string PackageName
    {
        get
        {
            return _previous.PackageName;
        }
    }
    public string Name
    {
        get
        {
            return _previous.Name;
        }
    }
    public string VersionString
    {
        get
        {
            return _previous.VersionString;
        }
    }
    public Version Version
    {
        get
        {
            return _previous.Version;
        }
    }
    public string BuildString
    {
        get
        {
            return _previous.BuildString;
        }
    }
    public AppPackagingModel PackagingModel
    {
        get
        {
            return _previous.PackagingModel;
        }
    }
    public LayoutDirection RequestedLayoutDirection
    {
        get
        {
            return _previous.RequestedLayoutDirection;
        }
    }
    public void ShowSettingsUI()
    {
        _previous.ShowSettingsUI();
    }

    public void Dispose()
    {
        if (ReferenceEquals(AppInfo.Current, this))
        {
            SetCurrent(_previous);
        }
    }
}