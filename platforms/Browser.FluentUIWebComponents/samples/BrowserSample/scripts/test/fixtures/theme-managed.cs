using System.Reflection;
using FluentBrowserSample.Services;
using FluentBrowserSample.ViewModels;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

internal static class ThemeAssertions
{
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Run()
    {
        // Substitute only the platform theme source. Application, Picker,
        // bindings and the sample ViewModel are the actual production types.
        var previous = AppInfo.Current;
        var previousDispatcher = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new TestDispatcherProvider());
        var setCurrent = typeof(AppInfo).GetMethod("SetCurrent", BindingFlags.Static | BindingFlags.NonPublic)!;
        var platform = new TestAppInfo { RequestedTheme = AppTheme.Light };
        setCurrent.Invoke(null, new object[] { platform });
        var vm = new CardStudioViewModel(new CardPresetService());
        try
        {
            var app = new Application();
            var picker = new Picker { BindingContext = vm };
            picker.SetBinding(Picker.ItemsSourceProperty, new Binding("Theme.Choices", mode: BindingMode.OneWay));
            picker.SetBinding(Picker.SelectedIndexProperty, new Binding("Theme.SelectedIndex", mode: BindingMode.TwoWay));
            vm.Theme.Connect(app);
            Check(picker.Items.SequenceEqual(new[] { "System (Light)", "Light", "Dark" }) && picker.SelectedIndex == 0,
                "Initial observable options and selection must reach the bound Picker.");
            picker.SelectedIndex = 1;
            Check(app.UserAppTheme == AppTheme.Light && vm.Theme.SelectedIndex == 1 && picker.Items[0] == "System",
                "Picker TwoWay binding must select Light and update the System label.");
            int effectiveChanges = 0;
            app.RequestedThemeChanged += (_, _) => effectiveChanges++;
            picker.SelectedIndex = 0;
            Check(effectiveChanges == 0 && picker.Items[0] == "System (Light)" && picker.SelectedIndex == 0,
                "Same-effective-theme return to System must refresh without an event.");

            void ChangeSystem(AppTheme theme)
            {
                platform.RequestedTheme = theme;
                ((IApplication)app).ThemeChanged();
            }

            ChangeSystem(AppTheme.Dark);
            Check(picker.Items[0] == "System (Dark)" && picker.SelectedIndex == 0 && vm.Theme.SelectedIndex == 0,
                "Replacing the selected label must not clear the bound selection.");
            picker.SelectedIndex = 2;
            ChangeSystem(AppTheme.Light);
            Check(picker.Items[0] == "System" && picker.SelectedIndex == 2 && app.RequestedTheme == AppTheme.Dark,
                "System changes under an override must not alter the preference or label.");
            picker.SelectedIndex = 0;
            Check(picker.Items[0] == "System (Light)" && app.RequestedTheme == AppTheme.Light,
                "Returning to System must read the latest platform theme.");
            vm.Theme.SelectedIndex = 2;
            Check(picker.SelectedIndex == 2 && app.UserAppTheme == AppTheme.Dark,
                "Programmatic ViewModel writes must update the Picker and application.");
            app.UserAppTheme = AppTheme.Light;
            Check(picker.SelectedIndex == 1 && vm.Theme.SelectedIndex == 1,
                "Application effective-theme changes must update the ViewModel and Picker.");
            vm.Theme.SelectedIndex = -1;
            vm.Theme.SelectedIndex = 99;
            Check(app.UserAppTheme == AppTheme.Light && vm.Theme.SelectedIndex == 1, "Ignore transient and invalid indices.");

            vm.Theme.SelectedIndex = 0;
            vm.Theme.Disconnect();
            ChangeSystem(AppTheme.Dark);
            Check(picker.Items[0] == "System (Light)", "Disconnected ViewModels must not receive theme events.");
            vm.Theme.SelectedIndex = 2;
            Check(app.UserAppTheme == AppTheme.Unspecified, "Disconnected ViewModels must not write application state.");
            vm.Theme.Connect(app);
            Check(picker.Items[0] == "System (Dark)" && picker.SelectedIndex == 0, "Reconnection refreshes current state.");
            vm.Theme.Connect(app);
            int notifications = 0;
            vm.Theme.PropertyChanged += (_, _) => notifications++;
            ChangeSystem(AppTheme.Light);
            Check(notifications == 1, "Repeated Connect must not duplicate subscriptions.");
            vm.Theme.Disconnect();
            ChangeSystem(AppTheme.Dark);
            Check(notifications == 1, "Disconnect must remove the subscription.");
        }
        finally
        {
            vm.Theme.Disconnect();
            setCurrent.Invoke(null, new object[] { previous });
            Application.Current = null;
            DispatcherProvider.SetCurrent(previousDispatcher);
        }
    }

    private sealed class TestDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new TestDispatcher();
    }

    private sealed class TestDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action)
        {
            action();
            return true;
        }
        public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException();
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }

    private sealed class TestAppInfo : IAppInfo
    {
        public AppTheme RequestedTheme { get; set; }
        public string PackageName => "test";
        public string Name => "test";
        public string VersionString => "1.0";
        public Version Version => new(1, 0);
        public string BuildString => "1";
        public AppPackagingModel PackagingModel => AppPackagingModel.Unpackaged;
        public LayoutDirection RequestedLayoutDirection => LayoutDirection.LeftToRight;
        public void ShowSettingsUI() { }
    }
}