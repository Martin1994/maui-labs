using System.Globalization;
using FluentBrowserSample.ViewModels;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage : ContentPage
{
    private readonly CardStudioViewModel _viewModel;
    private int _technologyColumns = -1;
    private bool _diagnosticsStarted;
    public MainPage(CardStudioViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ConnectBoard();
        if (Application.Current is { } app)
        {
            _viewModel.Theme.Connect(app);
        }
    }

    protected override void OnDisappearing()
    {
        StopPlayback("Playback is off. Motion starts only when you choose Play.");
        DisconnectBoard();
        _viewModel.Theme.Disconnect();
        base.OnDisappearing();
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
        if (TechnologyGrid is null || columns == _technologyColumns)
        {
            return;
        }
        _technologyColumns = columns;
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