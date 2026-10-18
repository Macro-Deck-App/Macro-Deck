using MacroDeck.Ui.Components;
using MacroDeckHost.Integrations.DiscordWebhooks.Actions;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class DiscordMarkdownTests
{
	private static DiscordMarkdown Markdown(Dictionary<string, string>? names = null)
	{
		return new DiscordMarkdown(names ?? new Dictionary<string, string>(StringComparer.Ordinal));
	}

	private static UiTextSpan Span(IEnumerable<UiTextSpan> spans, string text)
	{
		return spans.Single(span => span.Text == text);
	}

	[Test]
	public void Inline_formatting_becomes_styled_spans()
	{
		var spans = Markdown().RenderInline("**bold** *italic* __under__ ~~gone~~ `code` ||secret||");

		Assert.Multiple(() =>
		{
			Assert.That(Span(spans, "bold").Weight, Is.EqualTo(UiComponentTextWeights.Bold));
			Assert.That(Span(spans, "italic").Italic, Is.True);
			Assert.That(Span(spans, "under").Underline, Is.True);
			Assert.That(Span(spans, "gone").Strikethrough, Is.True);
			Assert.That(Span(spans, "code").Monospace, Is.True);
			Assert.That(Span(spans, "code").Background, Is.Not.Null);
			Assert.That(Span(spans, "secret").Background, Is.Not.Null);
			Assert.That(DiscordMarkdown.PlainText(spans), Is.EqualTo("bold italic under gone code secret"));
		});
	}

	[Test]
	public void Styles_nest()
	{
		var spans = Markdown().RenderInline("**bold *both***");

		var both = Span(spans, "both");
		Assert.Multiple(() =>
		{
			Assert.That(both.Weight, Is.EqualTo(UiComponentTextWeights.Bold));
			Assert.That(both.Italic, Is.True);
		});
	}

	[Test]
	public void Underscores_inside_words_stay_literal()
	{
		var spans = Markdown().RenderInline("snake_case_name");

		Assert.That(DiscordMarkdown.PlainText(spans), Is.EqualTo("snake_case_name"));
	}

	[Test]
	public void Mentions_become_pills_with_their_preview_names()
	{
		var markdown = Markdown(new Dictionary<string, string> { ["42"] = "Ada" });

		var spans = markdown.RenderInline("<@42> <@&7> @everyone");

		Assert.Multiple(() =>
		{
			Assert.That(Span(spans, "@Ada").Background, Is.Not.Null);
			Assert.That(Span(spans, "@7").Background, Is.Not.Null, "an unnamed role shows its id");
			Assert.That(Span(spans, "@everyone").Background, Is.Not.Null);
			Assert.That(markdown.Mentions.Select(m => m.Kind),
				Is.EqualTo(new[] { DiscordMentionKind.User, DiscordMentionKind.Role, DiscordMentionKind.Everyone }));
		});
	}

	[Test]
	public void Links_are_coloured_and_underlined()
	{
		var spans = Markdown().RenderInline("see [docs](https://example.invalid/d) or https://example.invalid/x.");

		Assert.Multiple(() =>
		{
			Assert.That(Span(spans, "docs").Underline, Is.True);
			Assert.That(Span(spans, "docs").Color, Is.EqualTo(DiscordPreviewPalette.Link));
			Assert.That(Span(spans, "https://example.invalid/x").Underline, Is.True);
		});
	}

	[Test]
	public void Blocks_keep_line_breaks_and_get_their_variant()
	{
		var blocks = Markdown()
			.RenderBlocks("# Title\nline one\nline two\n> quoted\n```cs\nvar x = 1;\n```\n-# small print");

		Assert.Multiple(() =>
		{
			Assert.That(blocks.Select(b => b.Variant),
				Is.EqualTo(new[]
				{
					"heading-1",
					null,
					DiscordMarkdown.QuoteVariant,
					DiscordMarkdown.CodeBlockVariant,
					DiscordMarkdown.SubtextVariant
				}));
			Assert.That(blocks[1].Text, Is.EqualTo("line one\nline two"));
			Assert.That(blocks[2].Text, Is.EqualTo("quoted"));
			Assert.That(blocks[3].Text, Is.EqualTo("var x = 1;"));
			Assert.That(blocks[3].Spans.Single().Monospace, Is.True);
		});
	}

	[Test]
	public void Escaped_markers_are_literal()
	{
		Assert.That(DiscordMarkdown.PlainText(Markdown().RenderInline(@"\*not italic\*")), Is.EqualTo("*not italic*"));
	}
}
