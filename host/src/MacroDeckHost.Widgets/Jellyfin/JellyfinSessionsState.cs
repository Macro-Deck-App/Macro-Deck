using System.Text.Json;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Jellyfin;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.Jellyfin;

internal enum JellyfinSessionsStatus
{
	NoServer,
	NotConnected,
	Ready
}

internal sealed record JellyfinArtworkRef(string InstanceId, string ArtworkId);

internal sealed record JellyfinSessionRow(
	string Key,
	string Title,
	string? Subtitle,
	string Detail,
	bool IsPaused,
	double Progress)
{
	public bool IsAudio { get; init; }

	public JellyfinArtworkRef? ArtworkRef { get; init; }

	public UiResource? Artwork { get; init; }
}

internal sealed record JellyfinSessionsState(
	JellyfinSessionsStatus Status,
	IReadOnlyList<JellyfinSessionRow> Rows,
	UiResource? Glyph = null)
{
	public static JellyfinSessionsState Empty { get; } = new(JellyfinSessionsStatus.NoServer, []);

	public int Count => Rows.Count;

	public JellyfinSessionRow? Lead => Rows.Count > 0 ? Rows[0] : null;

	public string MoreBadge => Rows.Count > 1 ? $"+{Rows.Count - 1}" : string.Empty;

	public static JellyfinSessionsState Compute(
		IReadOnlyList<JellyfinServerSummary> servers,
		string? serverId,
		Func<JellyfinArtworkRef, UiResource?>? artwork = null,
		UiResource? glyph = null)
	{
		var selected = string.IsNullOrEmpty(serverId)
			? servers
			: [.. servers.Where(server => string.Equals(server.ServerId, serverId, StringComparison.OrdinalIgnoreCase))];

		if (selected.Count == 0)
		{
			return Empty with { Glyph = glyph };
		}

		if (!selected.Any(server => server.IsConnected))
		{
			return new JellyfinSessionsState(JellyfinSessionsStatus.NotConnected, [], glyph);
		}

		var rows = selected
			.SelectMany(server => server.ActiveSessions.Select(session => (Server: server, Session: session)))
			.OrderBy(pair => pair.Session.Playback == JellyfinSessionPlayback.Paused)
			.ThenByDescending(pair => pair.Session.LastActivity)
			.ThenBy(pair => pair.Session.Title, StringComparer.CurrentCultureIgnoreCase)
			.Select(pair =>
			{
				var reference = pair.Session is { ArtworkId: { } id, ArtworkInstanceId: { } instance }
					? new JellyfinArtworkRef(instance, id)
					: null;
				return new JellyfinSessionRow(RowKey(pair.Server.ServerId, pair.Session.Key),
					pair.Session.Title,
					pair.Session.Subtitle,
					Detail(pair.Session),
					pair.Session.Playback == JellyfinSessionPlayback.Paused,
					Math.Clamp((pair.Session.ProgressPercent ?? 0) / 100, 0, 1))
				{
					IsAudio = pair.Session.IsAudio,
					ArtworkRef = reference,
					Artwork = reference is null ? null : artwork?.Invoke(reference)
				};
			})
			.ToList();

		return new JellyfinSessionsState(JellyfinSessionsStatus.Ready, rows, glyph);
	}

	// UI node keys allow only ASCII letters, digits, dots, hyphens and underscores.
	private static string RowKey(string serverId, string sessionKey)
		=> "s-" + new string([
			.. $"{serverId}-{sessionKey}".Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')
		]);

	private static string Detail(JellyfinSessionSummary session)
		=> string.IsNullOrEmpty(session.UserName) ? session.DeviceName : $"{session.UserName} · {session.DeviceName}";
}

internal static class JellyfinSessionsSettings
{
	public static string? Server(JsonElement data) => WidgetConfigJson.ReadString(data, JellyfinWidgetTypes.ServerKey);

	public static string? BackgroundColor(JsonElement data)
		=> WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor"));
}
