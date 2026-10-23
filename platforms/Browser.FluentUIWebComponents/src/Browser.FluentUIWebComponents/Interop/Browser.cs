using System.Runtime.InteropServices.JavaScript;

namespace Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

internal static partial class Browser
{
    [JSImport("startTicker", "maui-browser")]
    internal static partial void StartTicker(int maxFps);
    [JSImport("stopTicker", "maui-browser")]
    internal static partial void StopTicker();
    [JSImport("subscribeInput", "maui-browser")]
    internal static partial void SubscribeInput(int id);
    [JSImport("inputValue", "maui-browser")]
    internal static partial void SetInputValue(int id, string value);
    [JSImport("inputSelection", "maui-browser")]
    internal static partial void SetInputSelection(int id, int start, int length);
    [JSImport("inputAttribute", "maui-browser")]
    internal static partial void SetInputAttribute(int id, string name, string? value);
    [JSImport("inputFocus", "maui-browser")]
    internal static partial bool FocusInput(int id, bool focused);
    [JSImport("getSystemTheme", "maui-browser")]
    [return: JSStringUnion(typeof(BrowserThemeName))]
    internal static partial string GetSystemTheme();
    [JSImport("getThemeColor", "maui-browser")]
    internal static partial string GetThemeColor(string token, [JSStringUnion(typeof(BrowserThemeName))] string theme);
    [JSImport("startThemeTracking", "maui-browser")]
    [return: JSStringUnion(typeof(BrowserThemeName))]
    internal static partial string StartThemeTracking();
    [JSImport("applyTheme", "maui-browser")]
    internal static partial void ApplyTheme([JSStringUnion(typeof(BrowserThemeName))] string theme);
    [JSImport("report", "maui-browser")]
    internal static partial void Report(string message);
    [JSImport("create", "maui-browser")]
    internal static partial int Create(string tagName);
    [JSImport("style", "maui-browser")]
    internal static partial void Style(int id, string name, string value);
    [JSImport("property", "maui-browser")]
    internal static partial void SetStringProperty(int id, string name, string? value);
    [JSImport("property", "maui-browser")]
    internal static partial void SetBooleanProperty(int id, string name, bool value);
    [JSImport("property", "maui-browser")]
    internal static partial void SetNumberProperty(int id, string name, double value);
    [JSImport("attribute", "maui-browser")]
    internal static partial void SetAttribute(int id, string name, string? value);
    [JSImport("frame", "maui-browser")]
    internal static partial void Frame(int id, double x, double y, double width, double height);
    [JSImport("measure", "maui-browser")]
    internal static partial double Measure(int id, double width, double height, bool returnHeight);
    [JSImport("insert", "maui-browser")]
    internal static partial void Insert(int parent, int child, int index);
    [JSImport("remove", "maui-browser")]
    internal static partial void Remove(int child);
    [JSImport("release", "maui-browser")]
    internal static partial void Release(int id);
    [JSImport("mount", "maui-browser")]
    internal static partial void Mount(int window);
    [JSImport("unmount", "maui-browser")]
    internal static partial void Unmount(int window);
    [JSImport("subscribeClick", "maui-browser")]
    internal static partial void SubscribeClick(int id);
    [JSImport("subscribeScroll", "maui-browser")]
    internal static partial void SubscribeScroll(int id);
    [JSImport("requestLayout", "maui-browser")]
    internal static partial void RequestLayout();
    [JSImport("stop", "maui-browser")]
    internal static partial void Stop();
    [JSImport("schedule", "maui-browser")]
    internal static partial void Schedule(int id, double milliseconds);
    [JSImport("cancelSchedule", "maui-browser")]
    internal static partial void CancelSchedule(int id);
}