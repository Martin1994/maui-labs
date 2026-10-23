using System.Collections.Specialized;
using FluentBrowserSample.Views;

namespace FluentBrowserSample;

public partial class MainPage
{
    private bool _boardSubscribed;
    private int _nextCardId;

    private void ConnectBoard()
    {
        if (_boardSubscribed)
        {
            return;
        }
        BoardItems.Children.Clear();
        foreach (var card in _viewModel.Cards)
        {
            BoardItems.Children.Add(new BoardCard(card) { AutomationId = $"board-card-{++_nextCardId}" });
        }
        ((INotifyCollectionChanged)_viewModel.Cards).CollectionChanged += OnCardsChanged;
        _boardSubscribed = true;
    }

    private void DisconnectBoard()
    {
        ((INotifyCollectionChanged)_viewModel.Cards).CollectionChanged -= OnCardsChanged;
        _boardSubscribed = false;
    }

    private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep the existing views on Move: this exercises actual child reordering,
        // rather than disguising every collection change as a full tree rebuild.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                BoardItems.Children.Insert(e.NewStartingIndex,
                    new BoardCard(_viewModel.Cards[e.NewStartingIndex]) { AutomationId = $"board-card-{++_nextCardId}" });
                break;
            case NotifyCollectionChangedAction.Remove:
                BoardItems.Children.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                var view = BoardItems.Children[e.OldStartingIndex];
                BoardItems.Children.RemoveAt(e.OldStartingIndex);
                BoardItems.Children.Insert(e.NewStartingIndex, view);
                break;
            default:
                DisconnectBoard();
                ConnectBoard();
                break;
        }
        if (_playing && !_viewModel.CanPlay)
        {
            StopPlayback("Playback stopped. Add at least two cards to play.");
        }
    }

    private void OnEntryCompleted(object? sender, EventArgs e)
    {
        if (_viewModel.AddCardCommand.CanExecute(null))
        {
            _viewModel.AddCardCommand.Execute(null);
        }
    }

    private void OnPresetClicked(object? sender, EventArgs e)
    {
        // MAUI executes the bound command before Clicked. Select its new title
        // as a view interaction, leaving the draft state in the ViewModel.
        TitleEntry.Focus();
        TitleEntry.CursorPosition = 0;
        TitleEntry.SelectionLength = TitleEntry.Text?.Length ?? 0;
    }
}