using MacroDeck.Ui.Model.References;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>The card the widget picker draws for the Music Player: a track playing, a third of the way
/// through. No provider is connected while somebody is choosing a widget type, so none is read.</summary>
internal static class MusicPlayerWidgetSample
{
	private const long DurationMs = 214_000;
	private const long PositionMs = 78_000;

	internal static async ValueTask<MusicPlayerViewState> BuildAsync(IWidgetSampleTextResolver text,
		TimeProvider timeProvider)
	{
		var track = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.MusicTrack()).ConfigureAwait(false);
		var artist = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.MusicArtist()).ConfigureAwait(false);
		var album = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.MusicAlbum()).ConfigureAwait(false);

		return new MusicPlayerViewState
		{
			IsLoading = false,
			IsConnected = true,
			IsPlaying = true,
			TrackName = track,
			ArtistName = artist,
			AlbumName = album,
			// No artwork: cover art is whatever the connected player happens to be showing, and the widget
			// already draws its own spinning disc in its place - which is the honest sample of a playing
			// track whose cover has not arrived.
			Position = UiProgressReference.Advancing(PositionMs, timeProvider.GetUtcNow(), DurationMs),
		};
	}
}
