using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample.ViewModels;

public sealed class ThemeViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<string> _choices = new() { "System", "Light", "Dark" };
    private Application? _application;
    private bool _updating;
    private int _selectedIndex;

    public ThemeViewModel()
    {
        Choices = new ReadOnlyObservableCollection<string>(_choices);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyObservableCollection<string> Choices { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_updating || value is < 0 or > 2 || _application is not { } app)
            {
                return;
            }
            _updating = true;
            try
            {
                app.UserAppTheme = value switch
                {
                    1 => AppTheme.Light,
                    2 => AppTheme.Dark,
                    _ => AppTheme.Unspecified
                };
            }
            finally
            {
                _updating = false;
                // Same-effective-theme preference changes raise no theme event.
                Refresh();
            }
        }
    }

    internal void Connect(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!ReferenceEquals(_application, application))
        {
            Disconnect();
            _application = application;
            application.RequestedThemeChanged += OnRequestedThemeChanged;
        }
        Refresh();
    }

    internal void Disconnect()
    {
        if (_application is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            _application = null;
        }
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (_updating || _application is not { } app)
        {
            return;
        }
        _updating = true;
        try
        {
            string system = app.UserAppTheme == AppTheme.Unspecified
                ? $"System ({app.RequestedTheme})"
                : "System";
            if (_choices[0] != system)
            {
                _choices[0] = system;
            }
            _selectedIndex = app.UserAppTheme switch
            {
                AppTheme.Light => 1,
                AppTheme.Dark => 2,
                _ => 0
            };
            // Replacing a choice can clear Picker.SelectedIndex through TwoWay
            // binding. Ignore that transient write above and re-notify even if
            // the canonical index is unchanged, restoring the bound selection.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
        }
        finally
        {
            _updating = false;
        }
    }
}