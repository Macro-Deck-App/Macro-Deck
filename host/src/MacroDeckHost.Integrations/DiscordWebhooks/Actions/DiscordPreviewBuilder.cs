using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks.Preview;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

// Images are never fetched, so opening the editor contacts no image host: a placeholder names the host.
internal static class DiscordPreviewBuilder
{
	private const int MaxInlineFieldsPerRow = 3;

	public static IReadOnlyList<UiElement> Build(DiscordMessageDraft draft, string time)
	{
		var markdown = new DiscordMarkdown(draft.Names);
		var items = new List<UiElement> { Header(draft.Username.Trim(), time) };

		if (draft.AvatarUrl.Trim().Length > 0)
		{
			items.Add(Subtext("avatar", Strings.AvatarFrom(HostOf(draft.AvatarUrl))));
		}

		var content = markdown.RenderBlocks(draft.Content);
		for (var i = 0; i < content.Count; i++)
		{
			items.Add(Prose(string.Create(CultureInfo.InvariantCulture, $"c{i}"), content[i]));
		}

		var visibleEmbeds = draft.Embeds.Where(embed => !embed.IsEmpty).ToList();
		for (var i = 0; i < visibleEmbeds.Count; i++)
		{
			items.Add(Embed(i, visibleEmbeds[i], markdown, time));
		}

		if (draft.Content.Trim().Length == 0 && visibleEmbeds.Count == 0)
		{
			items.Add(Subtext("empty", Strings.Empty()));
		}

		var silent = markdown.Mentions
			.Where(mention => mention.Kind != DiscordMentionKind.Channel && !Pings(draft, mention))
			.Select(mention => Label(draft, mention))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (silent.Count > 0)
		{
			items.Add(Subtext("silent", Strings.WillNotPing(string.Join(", ", silent))));
		}

		if (markdown.Mentions.Any(mention => mention.Kind is DiscordMentionKind.User or DiscordMentionKind.Role
			&& !draft.Names.ContainsKey(mention.Id)))
		{
			items.Add(Subtext("unnamed", Strings.UnnamedMentions()));
		}

		return items;
	}

	private static bool Pings(DiscordMessageDraft draft, DiscordMention mention)
	{
		return mention.Kind switch
		{
			DiscordMentionKind.User => draft.AllowedUsers.Contains(mention.Id, StringComparer.Ordinal)
				|| draft.PingUsers && draft.AllowedUsers.Count == 0,
			DiscordMentionKind.Role => draft.AllowedRoles.Contains(mention.Id, StringComparer.Ordinal)
				|| draft.PingRoles && draft.AllowedRoles.Count == 0,
			DiscordMentionKind.Everyone or DiscordMentionKind.Here => draft.PingEveryone,
			_ => false
		};
	}

	private static string Label(DiscordMessageDraft draft, DiscordMention mention)
	{
		return mention.Kind is DiscordMentionKind.Everyone or DiscordMentionKind.Here
			? mention.Id
			: "@" + (draft.Names.TryGetValue(mention.Id, out var name) ? name : mention.Id);
	}

	private static UiConfigStack Header(string name, string time)
	{
		var initial = name.Length > 0 ? char.ToUpper(name[0], CultureInfo.CurrentCulture).ToString() : "#";
		var avatar = new UiTextSpan
		{
			Text = $" {initial} ",
			Weight = UiComponentTextWeights.Bold,
			Color = DiscordPreviewPalette.AvatarText,
			Background = DiscordPreviewPalette.AvatarBackground
		};

		var who = name.Length > 0
			? new UiProse
			{
				Key = "who",
				Text = name,
				Spans = UiValue.Of<IReadOnlyList<UiTextSpan>>([
					avatar,
					UiTextSpan.FromText(" "),
					UiTextSpan.FromText(name, DiscordPreviewPalette.Heading, UiComponentTextWeights.SemiBold)
				])
			}
			: new UiProse { Key = "who", Text = Strings.DefaultUsername() };

		return new UiConfigStack
		{
			Key = "head",
			Direction = "horizontal",
			Children =
			[
				who,
				new UiProse { Key = "app", Text = Strings.AppBadge(), Variant = DiscordMarkdown.SubtextVariant },
				new UiProse { Key = "time", Text = Strings.TodayAt(time), Variant = DiscordMarkdown.SubtextVariant }
			]
		};
	}

	private static UiConfigStack Embed(int index, DiscordEmbedDraft embed, DiscordMarkdown markdown, string time)
	{
		var children = new List<UiElement>();

		if (embed.AuthorName.Trim().Length > 0)
		{
			var spans = new List<UiTextSpan>();
			if (embed.AuthorIconUrl.Trim().Length > 0)
			{
				spans.Add(IconPill(embed.AuthorIconUrl));
				spans.Add(UiTextSpan.FromText(" "));
			}

			spans.AddRange(markdown.RenderInline(embed.AuthorName,
				true,
				embed.AuthorUrl.Trim().Length > 0 ? DiscordPreviewPalette.Link : DiscordPreviewPalette.Heading));
			children.Add(new UiProse
			{
				Key = "author",
				Text = DiscordMarkdown.PlainText(spans),
				Spans = UiValue.Of<IReadOnlyList<UiTextSpan>>(spans)
			});
		}

		if (embed.Title.Trim().Length > 0)
		{
			var spans = markdown.RenderInline(embed.Title,
				true,
				embed.Url.Trim().Length > 0 ? DiscordPreviewPalette.Link : DiscordPreviewPalette.Heading);
			children.Add(new UiProse
			{
				Key = "title",
				Text = DiscordMarkdown.PlainText(spans),
				Spans = UiValue.Of(spans)
			});
		}

		var description = markdown.RenderBlocks(embed.Description);
		for (var i = 0; i < description.Count; i++)
		{
			children.Add(Prose(string.Create(CultureInfo.InvariantCulture, $"d{i}"), description[i]));
		}

		children.AddRange(Fields(embed.Fields, markdown));

		if (embed.Image.Trim().Length > 0)
		{
			children.Add(Subtext("image", Strings.ImageFrom(HostOf(embed.Image))));
		}

		if (embed.Thumbnail.Trim().Length > 0)
		{
			children.Add(Subtext("thumbnail", Strings.ThumbnailFrom(HostOf(embed.Thumbnail))));
		}

		var footer = Footer(embed, time);
		if (footer is not null)
		{
			children.Add(footer);
		}

		return new UiConfigStack
		{
			Key = string.Create(CultureInfo.InvariantCulture, $"e{index}"),
			Accent = DiscordMessageValidator.TryParseColor(embed.Color, out var color)
				? string.Create(CultureInfo.InvariantCulture, $"#{color:x6}")
				: DiscordPreviewPalette.EmbedAccent,
			Children = children
		};
	}

	private static List<UiElement> Fields(IReadOnlyList<DiscordFieldDraft> fields, DiscordMarkdown markdown)
	{
		var elements = new List<UiElement>();
		var row = new List<UiElement>();
		var rows = 0;

		void FlushRow()
		{
			if (row.Count == 0)
			{
				return;
			}

			elements.Add(new UiConfigStack
			{
				Key = string.Create(CultureInfo.InvariantCulture, $"r{rows++}"),
				Direction = "horizontal",
				Wrap = false,
				Children = [.. row]
			});
			row.Clear();
		}

		for (var i = 0; i < fields.Count; i++)
		{
			var field = Field(i, fields[i], markdown);
			if (!fields[i].Inline)
			{
				FlushRow();
				elements.Add(field);
				continue;
			}

			row.Add(field with { RowWeight = 1 });
			if (row.Count == MaxInlineFieldsPerRow)
			{
				FlushRow();
			}
		}

		FlushRow();
		return elements;
	}

	private static UiConfigStack Field(int index, DiscordFieldDraft field, DiscordMarkdown markdown)
	{
		var name = markdown.RenderInline(field.Name, true, DiscordPreviewPalette.Heading);
		var children = new List<UiElement>
		{
			new UiProse { Key = "name", Text = DiscordMarkdown.PlainText(name), Spans = UiValue.Of(name) }
		};

		var value = markdown.RenderBlocks(field.Value);
		for (var i = 0; i < value.Count; i++)
		{
			children.Add(Prose(string.Create(CultureInfo.InvariantCulture, $"v{i}"), value[i]));
		}

		return new UiConfigStack
		{
			Key = string.Create(CultureInfo.InvariantCulture, $"f{index}"), Children = children
		};
	}

	private static UiConfigStack? Footer(DiscordEmbedDraft embed, string time)
	{
		var hasText = embed.FooterText.Trim().Length > 0;
		var timestamp = Timestamp(embed.Timestamp, time);
		if (!hasText && timestamp is null)
		{
			return null;
		}

		var children = new List<UiElement>();
		if (hasText)
		{
			var spans = new List<UiTextSpan>();
			if (embed.FooterIconUrl.Trim().Length > 0)
			{
				spans.Add(IconPill(embed.FooterIconUrl));
				spans.Add(UiTextSpan.FromText(" "));
			}

			spans.Add(UiTextSpan.FromText(embed.FooterText));
			children.Add(new UiProse
			{
				Key = "text",
				Text = embed.FooterText,
				Spans = UiValue.Of<IReadOnlyList<UiTextSpan>>(spans),
				Variant = DiscordMarkdown.SubtextVariant
			});
		}

		if (timestamp is { } stamp)
		{
			children.Add(new UiProse { Key = "stamp", Text = stamp, Variant = DiscordMarkdown.SubtextVariant });
		}

		return new UiConfigStack { Key = "footer", Direction = "horizontal", Children = children };
	}

	private static LocalizedText? Timestamp(string value, string time)
	{
		if (value.Length == 0)
		{
			return null;
		}

		if (value == DiscordMessageMap.TimestampNow)
		{
			return Strings.TodayAt(time);
		}

		return DiscordMessageValidator.TryParseTimestamp(value, out var parsed)
			? parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
			: value;
	}

	private static UiTextSpan IconPill(string url)
	{
		var host = HostOf(url);
		var initial = host.Length > 0 ? char.ToUpper(host[0], CultureInfo.CurrentCulture).ToString() : "?";

		return new UiTextSpan
		{
			Text = $" {initial} ",
			Weight = UiComponentTextWeights.Bold,
			Color = DiscordPreviewPalette.AvatarText,
			Background = DiscordPreviewPalette.IconBackground
		};
	}

	private static string HostOf(string url)
	{
		var trimmed = url.Trim();
		if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
		{
			return uri.Host;
		}

		return trimmed.Length > 60 ? trimmed[..60] + "…" : trimmed;
	}

	private static UiProse Prose(string key, DiscordMarkdownBlock block)
	{
		return new UiProse
		{
			Key = key,
			Text = block.Text,
			Spans = UiValue.Of(block.Spans),
			Variant = block.Variant is null ? UiValue.None<string>() : UiValue.Of(block.Variant)
		};
	}

	private static UiProse Subtext(string key, LocalizedText text)
	{
		return new UiProse { Key = key, Text = text, Variant = DiscordMarkdown.SubtextVariant };
	}
}
