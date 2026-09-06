using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchVariables
{
	public const string Prefix = "twitch_";

	public static IReadOnlyList<string> Names { get; } =
	[
		"is_connected",
		"is_live",
		"stream_title",
		"stream_category",
		"viewer_count",
		"uptime_seconds",
		"follower_count",
		"subscriber_count",
		"subscriber_points",
		"display_name",
		"login",
		"emote_only",
		"followers_only",
		"followers_only_duration",
		"slow_mode",
		"slow_mode_wait_time",
		"subscriber_only",
		"unique_chat",
		"last_follower",
		"last_subscriber",
		"last_cheerer",
		"last_raider"
	];

	public static IReadOnlyList<VariableDefinition> Declare(string variableKey,
		VariableConfiguration? configuration = null)
	{
		var prefix = $"{Prefix}{variableKey}_";

		return
		[
			VariableDefinition.Eager($"{prefix}is_connected",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(5))
				with
				{
					DisplayName = MacroDeckStrings.Connection.Connected(), Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}is_live",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(10))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.IsLive(), Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}stream_title",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.StreamTitle(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}stream_category",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.StreamCategory(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}viewer_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.ViewerCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}uptime_seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(5))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.UptimeSeconds(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}follower_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.FollowerCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}subscriber_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(60))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.SubscriberCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}subscriber_points", VariableType.Numeric, 0, TimeSpan.FromSeconds(60))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.SubscriberPoints(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}display_name",
					VariableType.Text,
					refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.DisplayName(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}login", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.Login(), Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}emote_only",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.EmoteOnly(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}followers_only",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.FollowersOnly(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}followers_only_duration",
					VariableType.Numeric,
					decimalPlaces: 0,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.FollowersOnlyDuration(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}slow_mode",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.SlowMode(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}slow_mode_wait_time", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.SlowModeWaitTime(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}subscriber_only",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.SubscriberOnly(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}unique_chat",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.UniqueChat(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}last_follower",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.LastFollower(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}last_subscriber",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.LastSubscriber(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}last_cheerer",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.LastCheerer(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}last_raider",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.Twitch.Variables.LastRaider(),
					Configuration = configuration
				}
		];
	}

	public static object? Read(TwitchAccount account, TwitchAccountState state, string name)
		=> name switch
		{
			"is_connected" => state.IsConnected,
			"is_live" => state.IsLive,
			"stream_title" => state.StreamTitle,
			"stream_category" => state.StreamCategory,
			"viewer_count" => state.ViewerCount,
			"uptime_seconds" => Uptime(state),
			"follower_count" => state.FollowerCount,
			"subscriber_count" => state.SubscriberCount,
			"subscriber_points" => state.SubscriberPoints,
			"display_name" => account.DisplayName,
			"login" => account.Login,
			"emote_only" => state.ChatSettings?.EmoteOnly,
			"followers_only" => state.ChatSettings?.FollowersOnly,
			"followers_only_duration" => state.ChatSettings?.FollowersOnlyDurationMinutes,
			"slow_mode" => state.ChatSettings?.SlowMode,
			"slow_mode_wait_time" => state.ChatSettings?.SlowModeWaitSeconds,
			"subscriber_only" => state.ChatSettings?.SubscriberOnly,
			"unique_chat" => state.ChatSettings?.UniqueChat,
			"last_follower" => state.LastFollower,
			"last_subscriber" => state.LastSubscriber,
			"last_cheerer" => state.LastCheerer,
			"last_raider" => state.LastRaider,
			_ => null
		};

	// A definition id is the canonical name with '_' swapped for '-', and a canonical variable name can
	// never contain '-', so swapping the separator back recovers the name exactly.
	public static (string VariableKey, string Name)? SplitDefinitionId(string definitionId)
		=> Split(definitionId.Replace('-', '_'));

	public static (string VariableKey, string Name)? Split(string variableName)
	{
		if (!variableName.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return null;
		}

		var remainder = variableName[Prefix.Length..];
		foreach (var name in Names)
		{
			var suffix = $"_{name}";
			if (remainder.Length > suffix.Length && remainder.EndsWith(suffix, StringComparison.Ordinal))
			{
				return (remainder[..^suffix.Length], name);
			}
		}

		return null;
	}

	private static int? Uptime(TwitchAccountState state)
		=> state.IsLive == true && state.StreamStartedAt is { } started
			? (int)Math.Max(0, (DateTimeOffset.UtcNow - started).TotalSeconds)
			: null;
}
