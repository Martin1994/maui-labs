using FluentBrowserSample.Models;

namespace FluentBrowserSample.Services;

public sealed class CardPresetService : ICardPresetService
{
    public IReadOnlyList<CardDesign> Presets { get; } = Array.AsReadOnly(new[]
    {
        new CardDesign("Hello, browser.", "A little XAML. A lot of possibility.", "sample_bot.png"),
        new CardDesign("Built with .NET", "Familiar tools. A new place to run.", "sample_bot_vector.svg", UseCustomFont: true),
        new CardDesign("Make it yours.", "Your ideas, rendered with Fluent.", "sample_bot_vector.png")
    });
}