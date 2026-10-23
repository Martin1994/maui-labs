using FluentBrowserSample.Models;

namespace FluentBrowserSample.Services;

public interface ICardPresetService
{
    IReadOnlyList<CardDesign> Presets { get; }
}