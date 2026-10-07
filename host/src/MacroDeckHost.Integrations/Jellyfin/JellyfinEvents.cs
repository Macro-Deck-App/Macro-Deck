using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Events;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin.Params;

namespace MacroDeckHost.Integrations.Jellyfin;

internal static class JellyfinEventIds
{
	public const string PlaybackStarted = "playback-started";
	public const string PlaybackPaused = "playback-paused";
	public const string PlaybackResumed = "playback-resumed";
	public const string PlaybackStopped = "playback-stopped";
	public const string ItemChanged = "item-changed";
	public const string SessionConnected = "session-connected";
	public const string SessionDisconnected = "session-disconnected";
	public const string Connected = "connected";
	public const string Disconnected = "disconnected";
}

internal static class JellyfinEventPayload
{
	public const string Configuration = "configuration";
	public const string Server = "server";
	public const string SessionId = "sessionId";
	public const string DeviceId = "deviceId";
	public const string Device = "device";
	public const string Client = "client";
	public const string User = "user";
	public const string ItemId = "itemId";
	public const string ItemName = "itemName";
	public const string ItemType = "itemType";
	public const string Series = "series";
	public const string Album = "album";
}

internal sealed record JellyfinEvent(string Id, IReadOnlyDictionary<string, object?> Payload);

internal static class JellyfinEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Session(JellyfinEventIds.PlaybackStarted, Strings.PlaybackStartedName(), Strings.PlaybackStartedDescription()),
		Session(JellyfinEventIds.PlaybackPaused, Strings.PlaybackPausedName(), Strings.PlaybackPausedDescription()),
		Session(JellyfinEventIds.PlaybackResumed, Strings.PlaybackResumedName(), Strings.PlaybackResumedDescription()),
		Session(JellyfinEventIds.PlaybackStopped, Strings.PlaybackStoppedName(), Strings.PlaybackStoppedDescription()),
		Session(JellyfinEventIds.ItemChanged, Strings.ItemChangedName(), Strings.ItemChangedDescription()),
		Session(JellyfinEventIds.SessionConnected,
			Strings.SessionConnectedName(),
			Strings.SessionConnectedDescription()),
		Session(JellyfinEventIds.SessionDisconnected,
			Strings.SessionDisconnectedName(),
			Strings.SessionDisconnectedDescription()),
		Server(JellyfinEventIds.Connected, MacroDeckStrings.Connection.Connected()),
		Server(JellyfinEventIds.Disconnected, MacroDeckStrings.Connection.Disconnected())
	];

	private static EventDefinition Session(string id, LocalizedText name, LocalizedText description) => new()
	{
		Id = id,
		Name = name,
		Description = description,
		Category = Strings.Category(),
		ConfigurationParameters = [ConfigurationParameter(), DeviceParameter()],
		PayloadParameters =
		[
			ConfigurationParameter(),
			ActionParameter.Text(JellyfinEventPayload.Server, label: Params.Server()),
			ActionParameter.Text(JellyfinEventPayload.SessionId, label: Params.Session()),
			DeviceParameter(),
			ActionParameter.Text(JellyfinEventPayload.Device, label: Params.Device()),
			ActionParameter.Text(JellyfinEventPayload.Client, label: Params.Client()),
			ActionParameter.Text(JellyfinEventPayload.User, label: Params.User()),
			ActionParameter.Text(JellyfinEventPayload.ItemId, label: Params.ItemId()),
			ActionParameter.Text(JellyfinEventPayload.ItemName, label: Params.ItemName()),
			ActionParameter.Text(JellyfinEventPayload.ItemType, label: Params.ItemType()),
			ActionParameter.Text(JellyfinEventPayload.Series, label: Params.Series()),
			ActionParameter.Text(JellyfinEventPayload.Album, label: Params.Album())
		]
	};

	private static EventDefinition Server(string id, LocalizedText name) => new()
	{
		Id = id,
		Name = name,
		Category = Strings.Category(),
		ConfigurationParameters = [ConfigurationParameter()],
		PayloadParameters =
		[
			ConfigurationParameter(),
			ActionParameter.Text(JellyfinEventPayload.Server, label: Params.Server())
		]
	};

	private static ActionParameter ConfigurationParameter()
		=> ActionParameter.DynamicChoice(JellyfinEventPayload.Configuration, label: Params.Server());

	private static ActionParameter DeviceParameter()
		=> ActionParameter.DynamicChoice(JellyfinEventPayload.DeviceId, label: Params.Device());
}

internal static class JellyfinEventDiff
{
	// The first successful read is a baseline: what was already playing when Macro Deck started is not news.
	// Sessions are tracked per device, so a client reconnecting under a new session id is not a disconnect.
	public static IReadOnlyList<JellyfinEvent> Compute(
		Guid entryId,
		string serverTitle,
		JellyfinServerState previous,
		JellyfinServerState current)
	{
		var events = new List<JellyfinEvent>();
		var wasDown = previous.Status is JellyfinConnectionStatus.Connecting or JellyfinConnectionStatus.Disconnected
			or JellyfinConnectionStatus.AuthenticationFailed;
		var isDown = current.Status is JellyfinConnectionStatus.Disconnected
			or JellyfinConnectionStatus.AuthenticationFailed;
		if (wasDown && current.IsConnected)
		{
			events.Add(new JellyfinEvent(JellyfinEventIds.Connected, ServerPayload(entryId, serverTitle)));
		}
		else if (!wasDown && isDown && previous.LastSuccess is not null)
		{
			events.Add(new JellyfinEvent(JellyfinEventIds.Disconnected, ServerPayload(entryId, serverTitle)));
		}

		if (previous.LastSuccess is null || !current.IsConnected || ReferenceEquals(previous.Sessions, current.Sessions))
		{
			return events;
		}

		var before = ByDevice(previous.Sessions);
		var after = ByDevice(current.Sessions);

		foreach (var (deviceId, session) in after)
		{
			if (!before.TryGetValue(deviceId, out var old))
			{
				events.Add(SessionEvent(JellyfinEventIds.SessionConnected, entryId, serverTitle, session, session.NowPlaying));
				if (session.NowPlaying is not null)
				{
					events.Add(SessionEvent(JellyfinEventIds.PlaybackStarted, entryId, serverTitle, session, session.NowPlaying));
				}

				continue;
			}

			var oldItem = old.NowPlaying;
			var newItem = session.NowPlaying;
			if (oldItem is null && newItem is not null)
			{
				events.Add(SessionEvent(JellyfinEventIds.PlaybackStarted, entryId, serverTitle, session, newItem));
			}
			else if (oldItem is not null && newItem is null)
			{
				events.Add(SessionEvent(JellyfinEventIds.PlaybackStopped, entryId, serverTitle, old, oldItem));
			}
			else if (oldItem is not null && newItem is not null)
			{
				if (!string.Equals(oldItem.Id, newItem.Id, StringComparison.Ordinal))
				{
					events.Add(SessionEvent(JellyfinEventIds.ItemChanged, entryId, serverTitle, session, newItem));
				}
				else if (!old.IsPaused && session.IsPaused)
				{
					events.Add(SessionEvent(JellyfinEventIds.PlaybackPaused, entryId, serverTitle, session, newItem));
				}
				else if (old.IsPaused && !session.IsPaused)
				{
					events.Add(SessionEvent(JellyfinEventIds.PlaybackResumed, entryId, serverTitle, session, newItem));
				}
			}
		}

		foreach (var (deviceId, old) in before)
		{
			if (after.ContainsKey(deviceId))
			{
				continue;
			}

			if (old.NowPlaying is not null)
			{
				events.Add(SessionEvent(JellyfinEventIds.PlaybackStopped, entryId, serverTitle, old, old.NowPlaying));
			}

			events.Add(SessionEvent(JellyfinEventIds.SessionDisconnected, entryId, serverTitle, old, null));
		}

		return events;
	}

	private static Dictionary<string, JellyfinSession> ByDevice(IReadOnlyList<JellyfinSession> sessions)
		=> sessions
			.GroupBy(session => session.DeviceId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => group.OrderByDescending(session => session.IsActive)
					.ThenByDescending(session => session.LastActivity)
					.First(),
				StringComparer.Ordinal);

	private static Dictionary<string, object?> ServerPayload(Guid entryId, string serverTitle) => new()
	{
		[JellyfinEventPayload.Configuration] = entryId.ToString("D"),
		[JellyfinEventPayload.Server] = serverTitle
	};

	private static JellyfinEvent SessionEvent(
		string id,
		Guid entryId,
		string serverTitle,
		JellyfinSession session,
		JellyfinMediaItem? item)
	{
		var payload = ServerPayload(entryId, serverTitle);
		payload[JellyfinEventPayload.SessionId] = session.Id;
		payload[JellyfinEventPayload.DeviceId] = session.DeviceId;
		payload[JellyfinEventPayload.Device] = session.DeviceName;
		payload[JellyfinEventPayload.Client] = session.Client;
		payload[JellyfinEventPayload.User] = session.UserName ?? string.Empty;
		payload[JellyfinEventPayload.ItemId] = item?.Id ?? string.Empty;
		payload[JellyfinEventPayload.ItemName] = item?.Name ?? string.Empty;
		payload[JellyfinEventPayload.ItemType] = item?.Type ?? string.Empty;
		payload[JellyfinEventPayload.Series] = item?.SeriesName ?? string.Empty;
		payload[JellyfinEventPayload.Album] = item?.IsAudio == true ? item.Album ?? string.Empty : item?.SeasonName ?? string.Empty;
		return new JellyfinEvent(id, payload);
	}
}
