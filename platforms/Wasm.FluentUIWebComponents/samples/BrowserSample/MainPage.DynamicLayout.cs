using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage
{
    private int _items;

    private void OnAddItemClicked(object? sender, EventArgs e)
    {
        DynamicItems.Children.Add(new Label
        {
            Text = $"Dynamic item {++_items} — long text also participates in MAUI measurement.",
            AutomationId = $"dynamic-item-{_items}",
            FontSize = 13
        });
    }

    private void OnMoveItemClicked(object? sender, EventArgs e)
    {
        if (DynamicItems.Children.Count > 1)
        {
            var first = DynamicItems.Children[0];
            DynamicItems.Children.RemoveAt(0);
            DynamicItems.Children.Add(first);
        }
    }

    private void OnRemoveItemClicked(object? sender, EventArgs e)
    {
        if (DynamicItems.Children.Count > 0)
        {
            DynamicItems.Children.RemoveAt(DynamicItems.Children.Count - 1);
        }
    }
}