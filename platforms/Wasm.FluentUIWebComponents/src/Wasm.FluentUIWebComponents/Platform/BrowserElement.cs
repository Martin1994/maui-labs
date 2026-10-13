using System.Globalization;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Wasm.FluentUIWebComponents;

internal sealed class BrowserElement(string tagName)
{
    public int Id
    {
        get;
    } = Browser.Create(tagName);
    public void Style(string name, string value)
    {
        Browser.Style(Id, name, value);
    }
    public void Frame(Rect frame)
    {
        Browser.Frame(Id, frame.X, frame.Y, Math.Max(0, frame.Width), Math.Max(0, frame.Height));
    }
    public static string Number(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
    public static string Color(Color? color)
    {
        return color is null ? "" :
            FormattableString.Invariant($"rgba({color.Red * 255},{color.Green * 255},{color.Blue * 255},{color.Alpha})");
    }
}