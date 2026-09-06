using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed record DiscordSetActivityArgs
{
	[JsonPropertyName("pid")]
	public int Pid { get; init; }

	[JsonPropertyName("activity")]
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public DiscordActivity? Activity { get; init; }
}

internal sealed record DiscordActivity
{
	[JsonPropertyName("type")]
	public int? Type { get; init; }

	[JsonPropertyName("details")]
	public string? Details { get; init; }

	[JsonPropertyName("state")]
	public string? State { get; init; }

	[JsonPropertyName("timestamps")]
	public DiscordActivityTimestamps? Timestamps { get; init; }

	[JsonPropertyName("assets")]
	public DiscordActivityAssets? Assets { get; init; }
}

internal sealed record DiscordActivityTimestamps
{
	[JsonPropertyName("start")]
	public long? Start { get; init; }

	[JsonPropertyName("end")]
	public long? End { get; init; }
}

internal sealed record DiscordActivityAssets
{
	[JsonPropertyName("large_image")]
	public string? LargeImage { get; init; }

	[JsonPropertyName("large_text")]
	public string? LargeText { get; init; }

	[JsonPropertyName("small_image")]
	public string? SmallImage { get; init; }

	[JsonPropertyName("small_text")]
	public string? SmallText { get; init; }
}
