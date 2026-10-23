namespace FluentBrowserSample.Models;

public sealed record CardDesign(string Title, string Tagline, string Artwork, bool UseCustomFont = false)
{
	public string? FontFamily => UseCustomFont ? "OpenSansSample" : null;
}