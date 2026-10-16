namespace MacroDeckHost.Integrations.Jellyfin.Widgets;

// Every id and data key here is persisted in profiles and exports, so the spellings are frozen.
internal static class JellyfinWidgetTypes
{
	public const string OwnerId = "app.macro-deck.jellyfin";

	public const string SessionsLocalId = "sessions";

	public const string SessionsQualifiedId = OwnerId + "::" + SessionsLocalId;

	public const string ServerKey = "server";

	public const string BackgroundColorKey = "backgroundColor";

	public const string DefaultData = """{"server":""}""";

	public const string DataSchema =
		"""{"type":"object","properties":{"server":{"type":"string"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}""";
}

internal enum JellyfinSessionPlayback
{
	Playing,
	Paused
}

internal sealed record JellyfinSessionSummary(
	string Key,
	string? UserName,
	string DeviceName,
	string Client,
	string Title,
	string? Subtitle,
	JellyfinSessionPlayback Playback,
	double? ProgressPercent)
{
	public bool IsAudio { get; init; }

	public DateTimeOffset LastActivity { get; init; }

	public string? ArtworkId { get; init; }

	public string? ArtworkInstanceId { get; init; }
}

internal sealed record JellyfinServerSummary(
	string ServerId,
	string Title,
	bool IsConnected,
	IReadOnlyList<JellyfinSessionSummary> ActiveSessions);

internal interface IJellyfinSessionSource
{
	event EventHandler? SessionsChanged;

	IReadOnlyList<JellyfinServerSummary> GetServers();
}
