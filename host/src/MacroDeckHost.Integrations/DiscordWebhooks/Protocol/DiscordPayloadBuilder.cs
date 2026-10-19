using System.Globalization;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Protocol;

internal static class DiscordPayloadBuilder
{
	public static DiscordWebhookPayload Build(DiscordMessageDraft draft, DateTimeOffset now)
	{
		var embeds = draft.Embeds.Where(embed => !embed.IsEmpty).Select(embed => BuildEmbed(embed, now)).ToList();

		return new DiscordWebhookPayload
		{
			Content = OrNull(draft.Content),
			Username = OrNull(draft.Username),
			AvatarUrl = OrNull(draft.AvatarUrl),
			Embeds = embeds.Count == 0 ? null : embeds,
			AllowedMentions = BuildAllowedMentions(draft)
		};
	}

	public static string FormatTimestamp(DateTimeOffset value)
	{
		return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
	}

	private static DiscordAllowedMentionsPayload BuildAllowedMentions(DiscordMessageDraft draft)
	{
		var parse = new List<string>();
		if (draft.PingUsers && draft.AllowedUsers.Count == 0)
		{
			parse.Add(DiscordMessageMap.ParseUsers);
		}

		if (draft.PingRoles && draft.AllowedRoles.Count == 0)
		{
			parse.Add(DiscordMessageMap.ParseRoles);
		}

		if (draft.PingEveryone)
		{
			parse.Add(DiscordMessageMap.ParseEveryone);
		}

		return new DiscordAllowedMentionsPayload
		{
			Parse = parse,
			Users = draft.AllowedUsers.Count == 0 ? null : draft.AllowedUsers,
			Roles = draft.AllowedRoles.Count == 0 ? null : draft.AllowedRoles
		};
	}

	private static DiscordEmbedPayload BuildEmbed(DiscordEmbedDraft embed, DateTimeOffset now)
	{
		return new DiscordEmbedPayload
		{
			Title = OrNull(embed.Title),
			Description = OrNull(embed.Description),
			Url = OrNull(embed.Url),
			Color = DiscordMessageValidator.TryParseColor(embed.Color, out var color) ? color : null,
			Timestamp = Timestamp(embed.Timestamp, now),
			Author = embed.AuthorName.Trim().Length == 0
				? null
				: new DiscordEmbedAuthorPayload
				{
					Name = embed.AuthorName, Url = OrNull(embed.AuthorUrl), IconUrl = OrNull(embed.AuthorIconUrl)
				},
			Footer = embed.FooterText.Trim().Length == 0
				? null
				: new DiscordEmbedFooterPayload { Text = embed.FooterText, IconUrl = OrNull(embed.FooterIconUrl) },
			Image = Media(embed.Image),
			Thumbnail = Media(embed.Thumbnail),
			Fields = embed.Fields.Count == 0
				? null
				: embed.Fields
					.Select(field => new DiscordEmbedFieldPayload
					{
						Name = field.Name, Value = field.Value, Inline = field.Inline
					})
					.ToList()
		};
	}

	private static string? Timestamp(string value, DateTimeOffset now)
	{
		if (value.Length == 0)
		{
			return null;
		}

		if (value == DiscordMessageMap.TimestampNow)
		{
			return FormatTimestamp(now);
		}

		return DiscordMessageValidator.TryParseTimestamp(value, out var parsed) ? FormatTimestamp(parsed) : null;
	}

	private static DiscordEmbedMediaPayload? Media(string url)
	{
		return url.Trim().Length == 0 ? null : new DiscordEmbedMediaPayload { Url = url.Trim() };
	}

	private static string? OrNull(string value)
	{
		return value.Trim().Length == 0 ? null : value;
	}
}
