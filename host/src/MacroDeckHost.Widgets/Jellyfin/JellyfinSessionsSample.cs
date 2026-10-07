namespace MacroDeckHost.Widgets.Jellyfin;

internal static class JellyfinSessionsSample
{
	public static JellyfinSessionsState State { get; } = new(JellyfinSessionsStatus.Ready,
	[
		new JellyfinSessionRow("sample-1", "Big Buck Bunny", "2008", "Alex · Android TV", false, 0.42),
		new JellyfinSessionRow("sample-2", "Sintel", "2010", "Sam · Jellyfin Web", true, 0.18),
		new JellyfinSessionRow("sample-3", "Elephants Dream", "2006", "Robin · Jellyfin Media Player", false, 0.73),
	]);
}
