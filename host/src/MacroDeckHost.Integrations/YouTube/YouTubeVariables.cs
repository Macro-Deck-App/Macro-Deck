using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.YouTube;

internal static class YouTubeVariables
{
	public const string Prefix = "youtube_";

	public static IReadOnlyList<string> Names { get; } =
	[
		"display_name",
		"is_live",
		"stream_title",
		"viewer_count",
		"like_count",
		"subscriber_count",
		"uptime_seconds",
		"stream_thumbnail_url"
	];

	public static string AccountPrefix(string variableKey) => $"{Prefix}{variableKey}_";

	public static IReadOnlyList<VariableDefinition> Declare(string variableKey,
		VariableConfiguration? configuration = null)
	{
		var prefix = AccountPrefix(variableKey);

		return
		[
			VariableDefinition.Eager($"{prefix}display_name",
					VariableType.Text,
					refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.DisplayName(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}is_live",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(10))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.IsLive(), Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}stream_title",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.StreamTitle(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}viewer_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.ViewerCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}like_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.LikeCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}subscriber_count", VariableType.Numeric, 0, TimeSpan.FromSeconds(60))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.SubscriberCount(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}uptime_seconds", VariableType.Numeric, 0, TimeSpan.FromSeconds(5))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.UptimeSeconds(),
					Configuration = configuration
				},
			VariableDefinition.Eager($"{prefix}stream_thumbnail_url",
					VariableType.Text,
					refreshInterval: TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.YouTube.Variables.StreamThumbnailUrl(),
					Configuration = configuration
				}
		];
	}

	public static object? Read(YouTubeAccount account, YouTubeAccountState state, string name, DateTimeOffset now)
		=> name switch
		{
			"display_name" => state.ChannelTitle ?? account.Title,
			"is_live" => state.IsLive,
			"stream_title" => state.StreamTitle,
			"viewer_count" => state.IsLive == true ? state.ViewerCount : null,
			"like_count" => state.IsLive == true ? state.LikeCount : null,
			"subscriber_count" => state.SubscriberCount,
			"uptime_seconds" => Uptime(state, now),
			"stream_thumbnail_url" => state.IsLive == true ? state.StreamThumbnailUrl : null,
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

	private static long? Uptime(YouTubeAccountState state, DateTimeOffset now)
		=> state.IsLive == true && state.StreamStartedAt is { } started
			? (long)Math.Max(0, (now - started).TotalSeconds)
			: null;
}
