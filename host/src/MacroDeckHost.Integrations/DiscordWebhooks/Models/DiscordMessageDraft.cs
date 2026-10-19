namespace MacroDeckHost.Integrations.DiscordWebhooks.Models;

internal sealed record DiscordMessageDraft
{
	public string Content { get; init; } = string.Empty;

	public string Username { get; init; } = string.Empty;

	public string AvatarUrl { get; init; } = string.Empty;

	public bool PingUsers { get; init; }

	public bool PingRoles { get; init; }

	public bool PingEveryone { get; init; }

	public IReadOnlyList<string> AllowedUsers { get; init; } = [];

	public IReadOnlyList<string> AllowedRoles { get; init; } = [];

	public IReadOnlyList<DiscordEmbedDraft> Embeds { get; init; } = [];

	public IReadOnlyDictionary<string, string> Names { get; init; } =
		new Dictionary<string, string>(StringComparer.Ordinal);
}

internal sealed record DiscordEmbedDraft
{
	public string Title { get; init; } = string.Empty;

	public string Description { get; init; } = string.Empty;

	public string Url { get; init; } = string.Empty;

	public string Color { get; init; } = string.Empty;

	public string Timestamp { get; init; } = string.Empty;

	public string AuthorName { get; init; } = string.Empty;

	public string AuthorUrl { get; init; } = string.Empty;

	public string AuthorIconUrl { get; init; } = string.Empty;

	public string FooterText { get; init; } = string.Empty;

	public string FooterIconUrl { get; init; } = string.Empty;

	public string Image { get; init; } = string.Empty;

	public string Thumbnail { get; init; } = string.Empty;

	public IReadOnlyList<DiscordFieldDraft> Fields { get; init; } = [];

	public bool IsEmpty
		=> Title.Length == 0
			&& Description.Length == 0
			&& AuthorName.Length == 0
			&& FooterText.Length == 0
			&& Image.Length == 0
			&& Thumbnail.Length == 0
			&& Fields.Count == 0;
}

internal sealed record DiscordFieldDraft(string Name, string Value, bool Inline);
