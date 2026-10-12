using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage : ContentPage
{
    private int _count;
    private int _technologyColumns = 2;
    public MainPage()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
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
}