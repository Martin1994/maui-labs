using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Browser.FluentUIWebComponents;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Main()
    {
        using var host = (BrowserApplication)Activator.CreateInstance(typeof(BrowserApplication), nonPublic: true)!;
        typeof(BrowserApplication).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, host);
        using var app = MauiApp.CreateBuilder().UseFluentUIWebComponents().Build();
        var context = new MauiContext(app.Services);
        var state = new State();
        var picker = new Picker { BindingContext = state, Title = "Choose" };
        var items = new ObservableCollection<string> { "same", "same", "" };
        picker.ItemsSource = items;
        picker.SetBinding(Picker.SelectedIndexProperty, new Binding(nameof(State.Index), mode: BindingMode.TwoWay));
        var handler = new PickerHandler();
        handler.SetMauiContext(context);
        handler.SetVirtualView(picker);
        int id = handler.PlatformView.Id;
        Check(Browser.Tags[id] == "fluent-dropdown" && Browser.Items[id].SequenceEqual(items), "Real Fluent mapping and items.");
        Check(Browser.Selection[id] == 0, "Initial selection.");
        BrowserApplication.OnPickerSelection(id, 1);
        Check(picker.SelectedIndex == 1 && state.Index == 1, "Native selection must update TwoWay binding.");
        state.Index = 2;
        Check(picker.SelectedIndex == 2 && Browser.Selection[id] == 2, "Managed selection must reach JS.");
        items.RemoveAt(0);
        Check(Browser.Items[id].Count == 2 && Browser.Selection[id] == picker.SelectedIndex, "Collection mutation preserves canonical index.");
        picker.SelectedIndex = -1;
        Check(Browser.Selection[id] == -1, "No selection.");
        picker.IsEnabled = false;
        BrowserApplication.OnPickerSelection(id, 0);
        Check(picker.SelectedIndex == -1, "Disabled ignores input.");
        picker.IsEnabled = true;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex == 1)
            {
                picker.SelectedIndex = 0;
            }
        };
        BrowserApplication.OnPickerSelection(id, 1);
        Check(Browser.Selection[id] == 0, "Reentrant managed normalization must win.");
        BrowserApplication.OnPickerOpen(id, true);
        BrowserApplication.OnPickerFocus(id, true);
        Check(((IPicker)picker).IsOpen && picker.IsFocused, "Open/focus map to MAUI.");
        ((IElementHandler)handler).DisconnectHandler();
        Check(!picker.IsFocused && !((IPicker)picker).IsOpen, "Disconnect clears focus/open.");
        BrowserApplication.OnPickerSelection(id, 1);
        Check(picker.SelectedIndex == 0, "Stale handle cannot select.");
        handler.SetVirtualView(picker);
        Check(handler.PlatformView.Id != id && Browser.Selection[handler.PlatformView.Id] == 0, "Reconnect restores state.");

        var control = new Switch { BindingContext = state, IsEnabled = false };
        control.SetBinding(Switch.IsToggledProperty, new Binding(nameof(State.Checked), mode: BindingMode.TwoWay));
        var switchHandler = new SwitchHandler();
        switchHandler.SetMauiContext(context);
        switchHandler.SetVirtualView(control);
        int switchId = switchHandler.PlatformView.Id;
        Check(Browser.Tags[switchId] == "fluent-switch", "Must map standard MAUI Switch to Fluent switch.");
        Check(Browser.Attributes[(switchId, "disabled")] == "", "Initially disabled must survive connection.");
        Check(typeof(SwitchHandler).Assembly.GetExportedTypes().All(type => type.Name != "ToggleButton"), "Do not vend an extra control.");
        control.IsEnabled = true;
        Check(Browser.Attributes[(switchId, "disabled")] is null, "Enabling clears the disabled attribute.");
        int changed = 0;
        control.Toggled += (_, _) => changed++;
        BrowserApplication.OnSwitchChanged(switchId, true);
        Check(state.Checked && control.IsToggled && changed == 1, "Native change must update IsToggled and its TwoWay binding.");
        state.Checked = false;
        Check(!control.IsToggled && !Browser.Booleans[(switchId, "checked")] && changed == 2, "Programmatic write uses normal MAUI Toggled semantics.");
        state.Checked = false;
        Check(changed == 2, "Equal values must not raise duplicate events.");
        control.IsEnabled = false;
        BrowserApplication.OnSwitchChanged(switchId, true);
        Check(!control.IsToggled && changed == 2, "Disabled switch cannot mutate.");
        control.IsEnabled = true;
        control.OnColor = Microsoft.Maui.Graphics.Colors.Red;
        control.OffColor = Microsoft.Maui.Graphics.Colors.Blue;
        control.ThumbColor = Microsoft.Maui.Graphics.Colors.Green;
        Check(Browser.Styles[(switchId, "--maui-switch-track")] == BrowserElement.Color(control.OffColor), "OffColor mapping.");
        control.IsToggled = true;
        Check(Browser.Styles[(switchId, "--maui-switch-track")] == BrowserElement.Color(control.OnColor), "OnColor mapping.");
        Check(Browser.Styles[(switchId, "--maui-switch-thumb")] == BrowserElement.Color(control.ThumbColor), "ThumbColor mapping.");
        control.OnColor = null;
        control.ThumbColor = null;
        Check(Browser.Attributes[(switchId, "data-maui-switch-track")] is null
            && Browser.Attributes[(switchId, "data-maui-switch-thumb")] is null, "Clearing colors restores Fluent defaults.");
        control.IsToggled = false;
        control.Toggled += (_, _) =>
        {
            if (control.IsToggled)
            {
                control.IsToggled = false;
            }
        };
        BrowserApplication.OnSwitchChanged(switchId, true);
        Check(!Browser.Booleans[(switchId, "checked")], "Reentrant normalization must win.");
        BrowserApplication.OnSwitchFocus(switchId, true);
        Check(control.IsFocused, "Native focus maps to MAUI.");
        control.Toggled += (_, _) => ((IElementHandler)switchHandler).DisconnectHandler();
        BrowserApplication.OnSwitchChanged(switchId, true);
        BrowserApplication.OnSwitchChanged(switchId, true);
        Check(((IElementHandler)switchHandler).PlatformView is null && !control.IsFocused, "Disconnect and late events are safe.");
        var replacement = new Switch { IsToggled = true };
        switchHandler.SetVirtualView(replacement);
        int replacementId = switchHandler.PlatformView.Id;
        Check(replacementId != switchId && Browser.Booleans[(replacementId, "checked")], "Reconnect restores checked state.");
        replacement.Toggled += (_, _) => switchHandler.SetVirtualView(new Switch { IsToggled = true });
        BrowserApplication.OnSwitchChanged(replacementId, false);
        Check(Browser.Booleans[(replacementId, "checked")], "Reentrant view replacement keeps its own state.");
        ThemeNotifications(host);
        Console.WriteLine("Managed selection assertions passed");
    }

    private static void ThemeNotifications(BrowserApplication host)
    {
        using var provider = BrowserAppInfo.Install(Microsoft.Maui.ApplicationModel.AppTheme.Light);
        var app = new Application();
        // Only host setup uses reflection. Exercise the real AppInfo shim, MAUI
        // standard ThemeChanged/RequestedThemeChanged path with no browser involved.
        typeof(BrowserApplication).GetField("_appInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, provider);
        typeof(BrowserApplication).GetField("_application", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, app);
        int effectiveChanges = 0;
        Check(typeof(BrowserApplication).GetEvent("SystemThemeChanged") is null, "Do not add a sample-specific backend theme event.");
        app.RequestedThemeChanged += (_, _) => effectiveChanges++;
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Light;
        BrowserApplication.OnSystemThemeChanged("dark");
        Check(app.PlatformAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark, "Platform theme must stay current under Light override.");
        Check(app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && effectiveChanges == 0, "System notification must not override user preference.");
        BrowserApplication.OnSystemThemeChanged("dark");
        Check(effectiveChanges == 0, "Duplicate platform updates do not change the effective theme.");
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
        Check(app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark && effectiveChanges == 1, "System uses latest platform theme.");
        BrowserApplication.OnSystemThemeChanged("light");
        Check(app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && effectiveChanges == 2,
            "System mode notifies effective changes.");
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        BrowserApplication.OnSystemThemeChanged("dark");
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
        Check(app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark && effectiveChanges == 3,
            "Returning to System with the same effective theme is immediately readable without a new event.");
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Dark;
        BrowserApplication.OnSystemThemeChanged("light");
        Check(app.PlatformAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light
            && app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark && effectiveChanges == 3,
            "Platform theme must stay current under Dark override without effective-theme events.");
        app.UserAppTheme = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
        Check(app.RequestedTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && effectiveChanges == 4,
            "Returning to System uses the latest platform value rather than the old override.");
        host.Dispose();
        BrowserApplication.OnSystemThemeChanged("dark");
        Check(app.PlatformAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Light && effectiveChanges == 4,
            "Disposed host ignores late system theme changes.");
    }

    private sealed class State : INotifyPropertyChanged
    {
        private int _index;
        private bool _checked;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Index
        {
            get => _index;
            set
            {
                _index = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Index)));
            }
        }
        public bool Checked
        {
            get => _checked;
            set
            {
                _checked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
            }
        }
    }
}