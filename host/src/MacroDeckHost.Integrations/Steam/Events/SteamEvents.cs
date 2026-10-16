using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Steam.State;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Params;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Events;

namespace MacroDeckHost.Integrations.Steam.Events;

internal static class SteamEventIds
{
	public const string GameStarted = "game-started";
	public const string GameStopped = "game-stopped";
	public const string FriendOnline = "friend-online";
	public const string FriendOffline = "friend-offline";
	public const string FriendStartedGame = "friend-started-game";
	public const string PersonaChanged = "persona-changed";
}

internal static class SteamEventPayload
{
	public const string Configuration = "configuration";
	public const string GameId = "gameId";
	public const string GameName = "gameName";
	public const string DurationSeconds = "durationSeconds";
	public const string FriendId = "friendId";
	public const string FriendName = "friendName";
	public const string State = "state";
	public const string PreviousState = "previousState";

	public const string ThisComputer = "local";
}

internal sealed record SteamEvent(string Id, IReadOnlyDictionary<string, object?> Payload);

internal static class SteamEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Game(SteamEventIds.GameStarted, Strings.GameStarted.Name(), Strings.GameStarted.Description(), false),
		Game(SteamEventIds.GameStopped, Strings.GameStopped.Name(), Strings.GameStopped.Description(), true),
		Friend(SteamEventIds.FriendOnline, Strings.FriendOnline.Name(), Strings.FriendOnline.Description()),
		Friend(SteamEventIds.FriendOffline, Strings.FriendOffline.Name(), Strings.FriendOffline.Description()),
		Friend(SteamEventIds.FriendStartedGame,
			Strings.FriendStartedGame.Name(),
			Strings.FriendStartedGame.Description()),
		new()
		{
			Id = SteamEventIds.PersonaChanged,
			Name = Strings.PersonaChanged.Name(),
			Description = Strings.PersonaChanged.Description(),
			Category = Strings.Category(),
			IconName = "user",
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters =
			[
				ConfigurationParameter(),
				ActionParameter.Text(SteamEventPayload.State, Params.Status()),
				ActionParameter.Text(SteamEventPayload.PreviousState, Params.PreviousStatus())
			]
		}
	];

	private static EventDefinition Game(string id, LocalizedText name, LocalizedText description, bool withDuration)
	{
		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = Strings.Category(),
			IconName = withDuration ? "x" : "play",
			ConfigurationParameters = [ConfigurationParameter()],
			PayloadParameters =
			[
				ConfigurationParameter(),
				ActionParameter.Number(SteamEventPayload.GameId, Params.GameId()),
				ActionParameter.Text(SteamEventPayload.GameName, Params.GameName()),
				.. withDuration
					? [ActionParameter.Number(SteamEventPayload.DurationSeconds, Params.DurationSeconds())]
					: Array.Empty<ActionParameter>()
			]
		};
	}

	private static EventDefinition Friend(string id, LocalizedText name, LocalizedText description)
	{
		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = Strings.Category(),
			IconName = "user",
			ConfigurationParameters = [ConfigurationParameter(), FriendParameter()],
			PayloadParameters =
			[
				ConfigurationParameter(),
				FriendParameter(),
				ActionParameter.Text(SteamEventPayload.FriendName, Params.FriendName()),
				ActionParameter.Number(SteamEventPayload.GameId, Params.GameId()),
				ActionParameter.Text(SteamEventPayload.GameName, Params.GameName())
			]
		};
	}

	private static ActionParameter ConfigurationParameter()
	{
		return ActionParameter.DynamicChoice(SteamEventPayload.Configuration, Params.Account());
	}

	private static ActionParameter FriendParameter()
	{
		return ActionParameter.DynamicChoice(SteamEventPayload.FriendId, Params.Friend());
	}
}

internal static class SteamEventFactory
{
	public static SteamEvent GameStarted(string configuration, int appId, string name)
	{
		return new SteamEvent(SteamEventIds.GameStarted,
			new Dictionary<string, object?>
			{
				[SteamEventPayload.Configuration] = configuration,
				[SteamEventPayload.GameId] = appId,
				[SteamEventPayload.GameName] = name
			});
	}

	public static SteamEvent GameStopped(string configuration, int appId, string name, TimeSpan duration)
	{
		return new SteamEvent(SteamEventIds.GameStopped,
			new Dictionary<string, object?>
			{
				[SteamEventPayload.Configuration] = configuration,
				[SteamEventPayload.GameId] = appId,
				[SteamEventPayload.GameName] = name,
				[SteamEventPayload.DurationSeconds] = Math.Max(0L, (long)duration.TotalSeconds)
			});
	}

	public static SteamEvent PersonaChanged(string configuration, int previous, int current)
	{
		return new SteamEvent(SteamEventIds.PersonaChanged,
			new Dictionary<string, object?>
			{
				[SteamEventPayload.Configuration] = configuration,
				[SteamEventPayload.State] = SteamPersona.Id(current),
				[SteamEventPayload.PreviousState] = SteamPersona.Id(previous)
			});
	}

	public static IEnumerable<SteamEvent> FriendChanges(
		string configuration,
		IReadOnlyList<SteamFriend> previous,
		IReadOnlyList<SteamFriend> current)
	{
		var before = previous.ToDictionary(friend => friend.SteamId, StringComparer.Ordinal);
		foreach (var friend in current)
		{
			if (!before.TryGetValue(friend.SteamId, out var old))
			{
				continue;
			}

			if (!old.IsOnline && friend.IsOnline)
			{
				yield return Friend(SteamEventIds.FriendOnline, configuration, friend);
			}
			else if (old.IsOnline && !friend.IsOnline)
			{
				yield return Friend(SteamEventIds.FriendOffline, configuration, friend);
			}

			if (friend.GameId is { } gameId && gameId != old.GameId)
			{
				yield return Friend(SteamEventIds.FriendStartedGame, configuration, friend);
			}
		}
	}

	private static SteamEvent Friend(string id, string configuration, SteamFriend friend)
	{
		return new SteamEvent(id,
			new Dictionary<string, object?>
			{
				[SteamEventPayload.Configuration] = configuration,
				[SteamEventPayload.FriendId] = friend.SteamId,
				[SteamEventPayload.FriendName] = friend.Name,
				[SteamEventPayload.GameId] = friend.GameId ?? 0,
				[SteamEventPayload.GameName] = friend.GameName ?? string.Empty
			});
	}
}
