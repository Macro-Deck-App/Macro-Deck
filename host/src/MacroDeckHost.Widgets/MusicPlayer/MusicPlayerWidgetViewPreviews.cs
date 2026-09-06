using MacroDeck.Localization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Widgets.MusicPlayer;

internal static class MusicPlayerWidgetViewPreviews
{
	[UiPreview("Playing", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Playing() => Build(new MusicPlayerViewState
	{
		IsLoading = false,
		Label = LocalizedText.FromLiteral("Spotify"),
		IsConnected = true,
		IsPlaying = true,
		TrackName = "Nightbird",
		ArtistName = "The Wandering Sound",
		AlbumName = "Distant Shores",
	});

	[UiPreview("Paused", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Paused() => Build(new MusicPlayerViewState
	{
		IsLoading = false,
		Label = LocalizedText.FromLiteral("Spotify"),
		IsConnected = true,
		IsPaused = true,
		TrackName = "Nightbird",
		ArtistName = "The Wandering Sound",
		AlbumName = "Distant Shores",
	});

	[UiPreview("Not connected", Profile = UiPreviewProfiles.Widget)]
	public static UiElement NotConnected() => Build(new MusicPlayerViewState
	{
		IsLoading = false, Label = LocalizedText.FromLiteral("Spotify"), IsConnected = false,
	});

	private static UiElement Build(MusicPlayerViewState state)
	{
		var icons = MusicPlayerWidgetIcons.EnsureRegistered(WidgetPreviewResources.Store);

		return MusicPlayerWidgetView.Build(new UiState<MusicPlayerViewState>(state),
			new UiState<MusicPlayerWidgetData>(new MusicPlayerWidgetData()),
			icons);
	}
}
