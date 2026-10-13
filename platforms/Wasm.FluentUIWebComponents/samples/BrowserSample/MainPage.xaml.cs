using System.Globalization;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage : ContentPage
{
    private int _count;
    private int _technologyColumns = 2;
    private bool _diagnosticsStarted;
    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (!_diagnosticsStarted && width > 0 && height > 0)
        {
            _diagnosticsStarted = true;
            _ = ShowDiagnosticsAsync();
        }
        int columns = width >= 800 ? 4 : 2;
        if (TechnologyGrid is null || columns == _technologyColumns)
        {
            return;
        }
        _technologyColumns = columns;
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

    private void OnIncrementClicked(object? sender, EventArgs e)
    {
        CounterLabel.Text = $"Clicked {++_count} times";
    }
    private void OnResetClicked(object? sender, EventArgs e)
    {
        _count = 0;
        CounterLabel.Text = "Clicked 0 times";
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
            WasmSizeLabel.Text = $"Wasm: {size} MiB ({bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            WasmSizeLabel.Text = "Wasm size unavailable";
            Console.Error.WriteLine(exception);
        }
    }

    private void OnRepositoryClicked(object? sender, EventArgs e)
    {
        // Synchronous interop preserves the click's transient browser user activation.
        SampleDiagnostics.OpenExternal(SampleDiagnostics.RepositoryUrl);
    }
}