using System.Globalization;
using FluentBrowserSample.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage : ContentPage
{
    private readonly CardStudioViewModel _viewModel;
    private int _technologyColumns = -1;
    private bool _compactTheme;
    private bool _diagnosticsStarted;
    private Application? _themeApplication;
    public MainPage(CardStudioViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        UpdateThemeStatus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConnectBoard();
        if (_themeApplication is null && Application.Current is { } app)
        {
            _themeApplication = app;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
        UpdateThemeStatus();
    }

    protected override void OnDisappearing()
    {
        StopPlayback("Playback is off. Motion starts only when you choose Play.");
        DisconnectBoard();
        if (_themeApplication is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            _themeApplication = null;
        }
        base.OnDisappearing();
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        UpdateThemeStatus();
    }

    private void UpdateThemeStatus()
    {
        if (Application.Current is { } app)
        {
            string preference = app.UserAppTheme == AppTheme.Unspecified ? $"System ({app.RequestedTheme})" : app.RequestedTheme.ToString();
            ThemeStatusLabel.Text = $"Theme: {preference}";
        }
    }

    private void SetTheme(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.UserAppTheme = theme;
            // Same-effective-theme preference changes do not raise the theme event.
            UpdateThemeStatus();
        }
    }

    private void OnSystemThemeClicked(object? sender, EventArgs e)
    {
        SetTheme(AppTheme.Unspecified);
    }

    private void OnLightThemeClicked(object? sender, EventArgs e)
    {
        SetTheme(AppTheme.Light);
    }

    private void OnDarkThemeClicked(object? sender, EventArgs e)
    {
        SetTheme(AppTheme.Dark);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (!_diagnosticsStarted && width > 0 && height > 0)
        {
            _diagnosticsStarted = true;
            _ = ShowDiagnosticsAsync();
        }
        int columns = width >= 800 ? 4 : width >= 600 ? 2 : 1;
        bool compactTheme = width < 400;
        if (TechnologyGrid is null || (columns == _technologyColumns && compactTheme == _compactTheme))
        {
            return;
        }
        _technologyColumns = columns;
        _compactTheme = compactTheme;
        // Fluent buttons retain their native minimum width. Wrap the selector
        // using MAUI layout rather than clipping it or overriding Fluent CSS.
        ThemeButtons.ColumnDefinitions.Clear();
        ThemeButtons.RowDefinitions.Clear();
        for (int i = 0; i < (compactTheme ? 2 : 3); i++)
        {
            ThemeButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        ThemeButtons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (compactTheme)
        {
            ThemeButtons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumnSpan(SystemThemeButton, compactTheme ? 2 : 1);
        Grid.SetColumn(LightThemeButton, compactTheme ? 0 : 1);
        Grid.SetColumn(DarkThemeButton, compactTheme ? 1 : 2);
        Grid.SetRow(LightThemeButton, compactTheme ? 1 : 0);
        Grid.SetRow(DarkThemeButton, compactTheme ? 1 : 0);
        // Use the same breakpoint as the cards: inline header on wide screens,
        // stacked rows on narrow screens, with the selector right-aligned in both.
        bool wide = columns == 4;
        PageContent.Padding = wide ? new Thickness(28, 36) : new Thickness(16, 24);
        EditorGrid.ColumnDefinitions.Clear();
        EditorGrid.RowDefinitions.Clear();
        EditorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        EditorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide)
        {
            EditorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }
        else
        {
            EditorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumn(DraftPreview, wide ? 1 : 0);
        Grid.SetRow(DraftPreview, wide ? 0 : 1);
        MetricsGrid.ColumnDefinitions.Clear();
        MetricsGrid.RowDefinitions.Clear();
        MetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        MetricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide)
        {
            MetricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }
        else
        {
            MetricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumn(WasmMetricCard, wide ? 1 : 0);
        Grid.SetRow(WasmMetricCard, wide ? 0 : 1);
        HeaderGrid.ColumnDefinitions.Clear();
        HeaderGrid.RowDefinitions.Clear();
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        HeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (wide)
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        else
        {
            HeaderGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetColumn(ThemeSelector, wide ? 1 : 0);
        Grid.SetRow(ThemeSelector, wide ? 0 : 1);
        TechnologyGrid.ColumnDefinitions.Clear();
        TechnologyGrid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++)
        {
            TechnologyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }
        for (int i = 0; i < 4 / columns; i++)
        {
            TechnologyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (int i = 0; i < TechnologyGrid.Children.Count; i++)
        {
            var card = (BindableObject)TechnologyGrid.Children[i];
            Grid.SetColumn(card, i % columns);
            Grid.SetRow(card, i / columns);
        }
    }

    private async Task ShowDiagnosticsAsync()
    {
        try
        {
            var startup = await SampleDiagnostics.CaptureStartupAsync();
            StartupLabel.Text = $"Startup: {startup.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)} ms";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            StartupLabel.Text = "Startup timing unavailable";
            Console.Error.WriteLine(exception);
        }

        try
        {
            long bytes = await SampleDiagnostics.GetWasmFileSizeAsync();
            string size = (bytes / (1024d * 1024)).ToString("F2", CultureInfo.InvariantCulture);
            WasmSizeLabel.Text = $"Wasm Size: {size} MiB ({bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            WasmSizeLabel.Text = "Wasm Size: unavailable";
            Console.Error.WriteLine(exception);
        }
    }

    private void OnRepositoryClicked(object? sender, EventArgs e)
    {
        // Synchronous interop preserves the click's transient browser user activation.
        SampleDiagnostics.OpenExternal(SampleDiagnostics.RepositoryUrl);
    }

    private void OnReadmeClicked(object? sender, EventArgs e)
    {
        SampleDiagnostics.OpenExternal(SampleDiagnostics.ReadmeUrl);
    }
}