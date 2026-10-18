using System.Globalization;
using MacroDeck.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Models;

internal sealed record DiscordViolation(string Key, LocalizedText Message);

internal static class DiscordMessageValidator
{
	public const int ContentLimit = 2000;
	public const int EmbedLimit = 10;
	public const int TitleLimit = 256;
	public const int DescriptionLimit = 4096;
	public const int FieldLimit = 25;
	public const int FieldNameLimit = 256;
	public const int FieldValueLimit = 1024;
	public const int FooterLimit = 2048;
	public const int AuthorLimit = 256;
	public const int UsernameLimit = 80;
	public const int TotalEmbedTextLimit = 6000;
	public const int MentionIdLimit = 100;
	public const int MaxColor = 0xFFFFFF;

	private static readonly string[] _forbiddenUsernameParts = ["clyde", "discord"];

	// A value still holding a template is checked for length only: its final text exists once the action runs.
	public static IReadOnlyList<DiscordViolation> Validate(DiscordMessageDraft draft, bool allowTemplates)
	{
		var violations = new List<DiscordViolation>();

		if (draft.Content.Trim().Length == 0 && draft.Embeds.All(embed => embed.IsEmpty))
		{
			violations.Add(new DiscordViolation("empty", Strings.Validation.Empty()));
		}

		TooLong(violations, "content", Strings.Editor.ContentLabel(), draft.Content, ContentLimit);
		TooLong(violations, "username", Strings.Editor.UsernameLabel(), draft.Username, UsernameLimit);

		if (_forbiddenUsernameParts.Any(part => draft.Username.Contains(part, StringComparison.OrdinalIgnoreCase)))
		{
			violations.Add(new DiscordViolation("username-forbidden", Strings.Validation.UsernameForbidden()));
		}

		Url(violations, "avatar", Strings.Editor.AvatarLabel(), draft.AvatarUrl, true, allowTemplates);

		if (draft.Embeds.Count > EmbedLimit)
		{
			violations.Add(new DiscordViolation("embeds",
				Strings.Validation.TooManyEmbeds(draft.Embeds.Count, EmbedLimit)));
		}

		var totalText = 0;
		for (var i = 0; i < draft.Embeds.Count; i++)
		{
			totalText += ValidateEmbed(violations, draft.Embeds[i], i, allowTemplates);
		}

		if (totalText > TotalEmbedTextLimit)
		{
			violations.Add(new DiscordViolation("embed-text",
				Strings.Validation.EmbedTextTooLong(totalText, TotalEmbedTextLimit)));
		}

		Mentions(violations,
			"users",
			Strings.Editor.UserIdsLabel(),
			draft.PingUsers,
			draft.AllowedUsers,
			Strings.Validation.UserMentionConflict(),
			allowTemplates);
		Mentions(violations,
			"roles",
			Strings.Editor.RoleIdsLabel(),
			draft.PingRoles,
			draft.AllowedRoles,
			Strings.Validation.RoleMentionConflict(),
			allowTemplates);

		return violations;
	}

	public static bool IsHttpUrl(string value)
	{
		return Uri.TryCreate(value, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
	}

	public static bool TryParseColor(string value, out int color)
	{
		color = 0;
		var hex = value.Trim().TrimStart('#');
		if (hex.Length == 8)
		{
			hex = hex[..6];
		}

		if (hex.Length == 6
			&& int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out color))
		{
			return true;
		}

		return int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out color)
			&& color <= MaxColor;
	}

	public static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
	{
		return DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeLocal,
			out timestamp);
	}

	public static bool IsTemplated(string value)
	{
		return value.Contains("{{", StringComparison.Ordinal) || value.Contains("{%", StringComparison.Ordinal);
	}

	private static int ValidateEmbed(
		List<DiscordViolation> violations,
		DiscordEmbedDraft embed,
		int index,
		bool allowTemplates)
	{
		var number = index + 1;
		var owner = Strings.Editor.EmbedHeading(number);
		var key = string.Create(CultureInfo.InvariantCulture, $"embed-{index}");

		void Length(string suffix, LocalizedText part, string value, int limit)
		{
			TooLong(violations, key + suffix, Part(owner, part), value, limit);
		}

		void Link(string suffix, LocalizedText part, string value, bool image)
		{
			Url(violations, key + suffix, Part(owner, part), value, image, allowTemplates);
		}

		if (embed.IsEmpty)
		{
			violations.Add(new DiscordViolation(key + "-empty", Strings.Validation.EmbedEmpty(owner)));
		}

		Length("-title", Strings.Editor.TitleLabel(), embed.Title, TitleLimit);
		Length("-description", Strings.Editor.DescriptionLabel(), embed.Description, DescriptionLimit);
		Length("-author", Strings.Editor.AuthorNameLabel(), embed.AuthorName, AuthorLimit);
		Length("-footer", Strings.Editor.FooterTextLabel(), embed.FooterText, FooterLimit);

		Link("-url", Strings.Editor.UrlLabel(), embed.Url, false);
		Link("-author-url", Strings.Editor.AuthorUrlLabel(), embed.AuthorUrl, false);
		Link("-author-icon", Strings.Editor.AuthorIconLabel(), embed.AuthorIconUrl, true);
		Link("-footer-icon", Strings.Editor.FooterIconLabel(), embed.FooterIconUrl, true);
		Link("-image", Strings.Editor.ImageLabel(), embed.Image, true);
		Link("-thumbnail", Strings.Editor.ThumbnailLabel(), embed.Thumbnail, true);

		if (embed.Color.Length > 0
			&& !(allowTemplates && IsTemplated(embed.Color))
			&& !TryParseColor(embed.Color, out _))
		{
			violations.Add(new DiscordViolation(key + "-color", Strings.Validation.InvalidColor(owner)));
		}

		if (embed.Timestamp.Length > 0
			&& embed.Timestamp != DiscordMessageMap.TimestampNow
			&& !(allowTemplates && IsTemplated(embed.Timestamp))
			&& !TryParseTimestamp(embed.Timestamp, out _))
		{
			violations.Add(new DiscordViolation(key + "-timestamp", Strings.Validation.InvalidTimestamp(owner)));
		}

		if (embed.Fields.Count > FieldLimit)
		{
			violations.Add(new DiscordViolation(key + "-fields",
				Strings.Validation.TooManyFields(owner, embed.Fields.Count, FieldLimit)));
		}

		var text = embed.Title.Length + embed.Description.Length + embed.AuthorName.Length + embed.FooterText.Length;
		for (var j = 0; j < embed.Fields.Count; j++)
		{
			var field = embed.Fields[j];
			var fieldOwner = Strings.Validation.FieldRef(number, j + 1);
			var fieldKey = string.Create(CultureInfo.InvariantCulture, $"{key}-field-{j}");

			if (field.Name.Trim().Length == 0 || field.Value.Trim().Length == 0)
			{
				violations.Add(new DiscordViolation(fieldKey + "-incomplete",
					Strings.Validation.FieldIncomplete(fieldOwner)));
			}

			TooLong(violations,
				fieldKey + "-name",
				Part(fieldOwner, Strings.Editor.FieldNameLabel()),
				field.Name,
				FieldNameLimit);
			TooLong(violations,
				fieldKey + "-value",
				Part(fieldOwner, Strings.Editor.FieldValueLabel()),
				field.Value,
				FieldValueLimit);
			text += field.Name.Length + field.Value.Length;
		}

		return text;
	}

	private static void Mentions(
		List<DiscordViolation> violations,
		string key,
		LocalizedText label,
		bool pingAll,
		IReadOnlyList<string> ids,
		LocalizedText conflict,
		bool allowTemplates)
	{
		if (pingAll && ids.Count > 0)
		{
			violations.Add(new DiscordViolation(key + "-conflict", conflict));
		}

		if (ids.Count > MentionIdLimit)
		{
			violations.Add(new DiscordViolation(key + "-count",
				Strings.Validation.TooManyIds(label, ids.Count, MentionIdLimit)));
		}

		var invalid = ids.FirstOrDefault(id => !IsSnowflake(id) && !(allowTemplates && IsTemplated(id)));
		if (invalid is not null)
		{
			violations.Add(new DiscordViolation(key + "-invalid", Strings.Validation.InvalidId(label, invalid)));
		}
	}

	private static bool IsSnowflake(string id)
	{
		return id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit);
	}

	private static void TooLong(
		List<DiscordViolation> violations,
		string key,
		LocalizedText label,
		string value,
		int limit)
	{
		if (value.Length > limit)
		{
			violations.Add(new DiscordViolation(key + "-length",
				Strings.Validation.TooLong(label, value.Length, limit)));
		}
	}

	private static void Url(
		List<DiscordViolation> violations,
		string key,
		LocalizedText label,
		string value,
		bool image,
		bool allowTemplates)
	{
		if (value.Length == 0 || allowTemplates && IsTemplated(value))
		{
			return;
		}

		if (IsHttpUrl(value))
		{
			return;
		}

		if (!image)
		{
			violations.Add(new DiscordViolation(key + "-invalid", Strings.Validation.InvalidUrl(label)));
			return;
		}

		if (!value.StartsWith("attachment://", StringComparison.OrdinalIgnoreCase))
		{
			violations.Add(new DiscordViolation(key + "-invalid", Strings.Validation.InvalidImageUrl(label)));
		}
	}

	private static LocalizedText Part(LocalizedText owner, LocalizedText part)
	{
		return Strings.Validation.Part(owner, part);
	}
}
