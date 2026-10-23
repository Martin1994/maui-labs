using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluentBrowserSample.Models;
using FluentBrowserSample.Services;
using Microsoft.Maui.Controls;

namespace FluentBrowserSample.ViewModels;

public sealed class CardStudioViewModel : INotifyPropertyChanged
{
    public const int BoardCapacity = 6;
    private readonly ICardPresetService _presets;
    private readonly ObservableCollection<CardDesign> _cards = new();
    private string _title = "";
    private string _tagline = "";
    private string _artwork = "";
    private bool _useCustomFont;
    private string _boardMessage = "No cards yet. Choose Add card to keep a copy of your design.";
    private int _presetIndex;

    public CardStudioViewModel(ICardPresetService presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        if (presets.Presets.Count == 0)
        {
            throw new ArgumentException("At least one card preset is required.", nameof(presets));
        }
        _presets = presets;
        Cards = new ReadOnlyObservableCollection<CardDesign>(_cards);
        AddCardCommand = new Command(AddCard, () => !string.IsNullOrWhiteSpace(Title) && _cards.Count < BoardCapacity);
        NextPresetCommand = new Command(NextPreset);
        ResetCommand = new Command(() => LoadPreset(0));
        SelectRasterCommand = new Command(() => Artwork = "sample_bot.png");
        SelectVectorCommand = new Command(() => Artwork = "sample_bot_vector.svg");
        ToggleFontCommand = new Command(() => UseCustomFont = !UseCustomFont);
        MoveFirstCommand = new Command(MoveFirst, () => _cards.Count > 1);
        RemoveLastCommand = new Command(RemoveLast, () => _cards.Count > 0);
        LoadPreset(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ReadOnlyObservableCollection<CardDesign> Cards { get; }
    public Command AddCardCommand { get; }
    public Command NextPresetCommand { get; }
    public Command ResetCommand { get; }
    public Command SelectRasterCommand { get; }
    public Command SelectVectorCommand { get; }
    public Command ToggleFontCommand { get; }
    public Command MoveFirstCommand { get; }
    public Command RemoveLastCommand { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? ""))
            {
                AddCardCommand.ChangeCanExecute();
                OnPropertyChanged(nameof(DraftHint));
            }
        }
    }

    public string Tagline
    {
        get => _tagline;
        set => SetProperty(ref _tagline, value ?? "");
    }

    public string Artwork
    {
        get => _artwork;
        private set
        {
            if (SetProperty(ref _artwork, value))
            {
                OnPropertyChanged(nameof(IsRasterArtwork));
                OnPropertyChanged(nameof(IsVectorArtwork));
                OnPropertyChanged(nameof(ArtworkDescription));
            }
        }
    }

    public bool IsRasterArtwork => Artwork == "sample_bot.png";
    public bool IsVectorArtwork => Artwork is "sample_bot_vector.svg" or "sample_bot_vector.png";
    public string ArtworkDescription => IsRasterArtwork ? "Selected: .NET 10 submarine" : "Selected: .NET bot";

    public bool UseCustomFont
    {
        get => _useCustomFont;
        private set
        {
            if (SetProperty(ref _useCustomFont, value))
            {
                OnPropertyChanged(nameof(FontFamily));
                OnPropertyChanged(nameof(FontToggleText));
            }
        }
    }

    public string? FontFamily => UseCustomFont ? "OpenSansSample" : null;
    public string FontToggleText => UseCustomFont ? "Open Sans: on" : "Open Sans: off";

    public string DraftHint => string.IsNullOrWhiteSpace(Title)
        ? "Give your card a title before adding it."
        : "Live preview · the inputs and preview share one ViewModel.";

    public string BoardSummary => $"{_cards.Count} / {BoardCapacity} cards · in memory, just for this visit";
    public bool CanPlay => _cards.Count > 1;
    public string BoardMessage
    {
        get => _boardMessage;
        private set => SetProperty(ref _boardMessage, value);
    }

    private void NextPreset()
    {
        LoadPreset((_presetIndex + 1) % _presets.Presets.Count);
    }

    private void LoadPreset(int index)
    {
        _presetIndex = index;
        var preset = _presets.Presets[index];
        Title = preset.Title;
        Tagline = preset.Tagline;
        Artwork = preset.Artwork;
        UseCustomFont = preset.UseCustomFont;
    }

    private void AddCard()
    {
        // Guard the operation too: ICommand.Execute can be called directly by consumers.
        if (!AddCardCommand.CanExecute(null))
        {
            return;
        }
        _cards.Add(new CardDesign(Title.Trim(), Tagline.Trim(), Artwork, UseCustomFont));
        BoardMessage = $"Added “{Title.Trim()}”. Edits to the draft won't change this snapshot.";
        UpdateBoardState();
    }

    private void MoveFirst()
    {
        if (_cards.Count < 2)
        {
            return;
        }
        string title = _cards[0].Title;
        _cards.Move(0, _cards.Count - 1);
        BoardMessage = $"Moved “{title}” to the end.";
    }

    private void RemoveLast()
    {
        if (_cards.Count == 0)
        {
            return;
        }
        string title = _cards[^1].Title;
        _cards.RemoveAt(_cards.Count - 1);
        BoardMessage = _cards.Count == 0 ? "No cards yet. Choose Add card to keep a copy of your design." : $"Removed “{title}”.";
        UpdateBoardState();
    }

    private void UpdateBoardState()
    {
        AddCardCommand.ChangeCanExecute();
        MoveFirstCommand.ChangeCanExecute();
        RemoveLastCommand.ChangeCanExecute();
        OnPropertyChanged(nameof(BoardSummary));
        OnPropertyChanged(nameof(CanPlay));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}