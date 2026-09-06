using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Discord.Webhooks;

internal sealed record DiscordWebhookRequest
{
	[JsonPropertyName("content")]
	public string? Content { get; init; }

	[JsonPropertyName("username")]
	public string? Username { get; init; }

	[JsonPropertyName("avatar_url")]
	public string? AvatarUrl { get; init; }

	[JsonPropertyName("tts")]
	public bool? Tts { get; init; }

	[JsonPropertyName("embeds")]
	public IReadOnlyList<DiscordWebhookEmbed>? Embeds { get; init; }

	[JsonIgnore]
	public bool IsEmpty => string.IsNullOrWhiteSpace(Content) && (Embeds is null || Embeds.Count == 0);
}

internal sealed record DiscordWebhookEmbed
{
	[JsonPropertyName("title")]
	public string? Title { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("url")]
	public string? Url { get; init; }

	[JsonPropertyName("color")]
	public int? Color { get; init; }

	[JsonPropertyName("image")]
	public DiscordWebhookEmbedImage? Image { get; init; }

	[JsonPropertyName("thumbnail")]
	public DiscordWebhookEmbedImage? Thumbnail { get; init; }

	[JsonPropertyName("footer")]
	public DiscordWebhookEmbedFooter? Footer { get; init; }

	[JsonIgnore]
	public bool IsEmpty => string.IsNullOrWhiteSpace(Title) &&
		string.IsNullOrWhiteSpace(Description) &&
		Image is null &&
		Thumbnail is null &&
		Footer is null;
}

internal sealed record DiscordWebhookEmbedImage
{
	[JsonPropertyName("url")]
	public required string Url { get; init; }
}

internal sealed record DiscordWebhookEmbedFooter
{
	[JsonPropertyName("text")]
	public required string Text { get; init; }
}
