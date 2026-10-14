using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Jellyfin.Widgets;

internal static class JellyfinSessionsViewPreviews
{
	[UiPreview("One session", Profile = UiPreviewProfiles.Widget)]
	public static UiElement One()
	{
		return Build(JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[0]] });
	}

	[UiPreview("Several sessions", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Several()
	{
		return Build(JellyfinSessionsSample.State);
	}

	[UiPreview("Paused", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Paused()
	{
		return Build(JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[1]] });
	}

	[UiPreview("Nothing playing", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Idle()
	{
		return Build(new JellyfinSessionsState(JellyfinSessionsStatus.Ready, []));
	}

	[UiPreview("Server unreachable", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Unreachable()
	{
		return Build(new JellyfinSessionsState(JellyfinSessionsStatus.NotConnected, []));
	}

	private static UiElement Build(JellyfinSessionsState state)
	{
		return JellyfinSessionsView.Build(new UiState<JellyfinSessionsState>(state));
	}
}
