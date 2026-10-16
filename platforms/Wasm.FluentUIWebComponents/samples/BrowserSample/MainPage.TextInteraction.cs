using System.ComponentModel;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample;

public partial class MainPage
{
    private int _completed;

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (InputStatusLabel is not null)
        {
            InputStatusLabel.Text = $"Text: {e.NewTextValue}";
        }
    }

    private void OnEntryCompleted(object? sender, EventArgs e)
    {
        CompletedStatusLabel.Text = $"Completed: {++_completed}";
    }

    private void OnEntryFocusChanged(object? sender, FocusEventArgs e)
    {
        if (FocusStatusLabel is not null)
        {
            FocusStatusLabel.Text = $"Focus: {e.IsFocused}";
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (SelectionStatusLabel is not null && DemoEntry is not null &&
            (e.PropertyName == nameof(Entry.CursorPosition) || e.PropertyName == nameof(Entry.SelectionLength)))
        {
            SelectionStatusLabel.Text = $"Selection: {DemoEntry.CursorPosition} + {DemoEntry.SelectionLength}";
        }
    }

    private void OnSetInputClicked(object? sender, EventArgs e)
    {
        DemoEntry.Text = "Hello from C#";
    }

    private void OnSelectInputClicked(object? sender, EventArgs e)
    {
        DemoEntry.Focus();
        DemoEntry.CursorPosition = 0;
        DemoEntry.SelectionLength = DemoEntry.Text?.Length ?? 0;
    }

    private void OnClearInputClicked(object? sender, EventArgs e)
    {
        DemoEntry.Text = "";
    }
}