using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Protocol;

internal sealed record DiscordWebhookPayload
{
	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	[JsonPropertyName("content")]
	public string? Content { get; init; }

	[JsonPropertyName("username")]
	public string? Username { get; init; }

	[JsonPropertyName("avatar_url")]
	public string? AvatarUrl { get; init; }

	[JsonPropertyName("embeds")]
	public IReadOnlyList<DiscordEmbedPayload>? Embeds { get; init; }

	[JsonPropertyName("allowed_mentions")]
	public required DiscordAllowedMentionsPayload AllowedMentions { get; init; }
}

internal sealed record DiscordAllowedMentionsPayload
{
	[JsonPropertyName("parse")]
	public required IReadOnlyList<string> Parse { get; init; }

	[JsonPropertyName("users")]
	public IReadOnlyList<string>? Users { get; init; }

	[JsonPropertyName("roles")]
	public IReadOnlyList<string>? Roles { get; init; }
}

internal sealed record DiscordEmbedPayload
{
	[JsonPropertyName("title")]
	public string? Title { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("url")]
	public string? Url { get; init; }

	[JsonPropertyName("color")]
	public int? Color { get; init; }

	[JsonPropertyName("timestamp")]
	public string? Timestamp { get; init; }

	[JsonPropertyName("author")]
	public DiscordEmbedAuthorPayload? Author { get; init; }

	[JsonPropertyName("footer")]
	public DiscordEmbedFooterPayload? Footer { get; init; }

	[JsonPropertyName("image")]
	public DiscordEmbedMediaPayload? Image { get; init; }

	[JsonPropertyName("thumbnail")]
	public DiscordEmbedMediaPayload? Thumbnail { get; init; }

	[JsonPropertyName("fields")]
	public IReadOnlyList<DiscordEmbedFieldPayload>? Fields { get; init; }
}

internal sealed record DiscordEmbedAuthorPayload
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("url")]
	public string? Url { get; init; }

	[JsonPropertyName("icon_url")]
	public string? IconUrl { get; init; }
}

internal sealed record DiscordEmbedFooterPayload
{
	[JsonPropertyName("text")]
	public required string Text { get; init; }

	[JsonPropertyName("icon_url")]
	public string? IconUrl { get; init; }
}

internal sealed record DiscordEmbedMediaPayload
{
	[JsonPropertyName("url")]
	public required string Url { get; init; }
}

internal sealed record DiscordEmbedFieldPayload
{
	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("value")]
	public required string Value { get; init; }

	[JsonPropertyName("inline")]
	public bool Inline { get; init; }
}
