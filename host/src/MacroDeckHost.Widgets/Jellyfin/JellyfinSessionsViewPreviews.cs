using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.Jellyfin;

internal static class JellyfinSessionsViewPreviews
{
	[UiPreview("One session", Profile = UiPreviewProfiles.Widget)]
	public static UiElement One()
		=> Build(JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[0]] });

	[UiPreview("Several sessions", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Several() => Build(JellyfinSessionsSample.State);

	[UiPreview("Paused", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Paused()
		=> Build(JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[1]] });

	[UiPreview("Nothing playing", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Idle() => Build(new JellyfinSessionsState(JellyfinSessionsStatus.Ready, []));

	[UiPreview("Server unreachable", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unreachable() => Build(new JellyfinSessionsState(JellyfinSessionsStatus.NotConnected, []));

	private static UiElement Build(JellyfinSessionsState state)
		=> JellyfinSessionsView.Build(new UiState<JellyfinSessionsState>(state));
}
