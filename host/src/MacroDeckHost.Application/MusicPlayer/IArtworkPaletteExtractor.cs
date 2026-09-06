namespace MacroDeckHost.Application.MusicPlayer;

/// <param name="Accent">A brightened tint of the artwork's average colour, as <c>#rrggbb</c>.</param>
/// <param name="Background">A darkened tint of the same average, as <c>#rrggbb</c>.</param>
public sealed record ArtworkPalette(string Accent, string Background);

/// <summary>
/// Derives the two colours a Music Player widget paints itself in from the cover it is showing.
///
/// <para>
/// Host-side rather than client-side since #749: a widget tree carries literal colours, so a renderer
/// that had to average the artwork itself would be a renderer that knows what a Music Player is. Doing it
/// once here also means eight clients showing one deck no longer decode and average the same cover eight
/// times.
/// </para>
/// </summary>
public interface IArtworkPaletteExtractor
{
	/// <summary>Reads <paramref name="image" />'s average colour and derives the pair from it. Returns
	/// <c>null</c> for bytes that are not a decodable image, which leaves the widget on its theme colours
	/// rather than failing the session.</summary>
	ArtworkPalette? Extract(byte[] image);
}
