using System.Text;
using System.Text.RegularExpressions;
using MacroDeck.Ui.Components;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal sealed record DiscordMarkdownBlock(string? Variant, IReadOnlyList<UiTextSpan> Spans, string Text);

internal enum DiscordMentionKind
{
	User,
	Role,
	Channel,
	Everyone,
	Here
}

internal sealed record DiscordMention(DiscordMentionKind Kind, string Id);

internal sealed partial class DiscordMarkdown
{
	public const string QuoteVariant = "quote";
	public const string CodeBlockVariant = "code-block";
	public const string SubtextVariant = "subtext";

	private const string Fence = "```";

	private static readonly (string Literal, DiscordMentionKind Kind)[] _broadcastMentions =
	[
		("@everyone", DiscordMentionKind.Everyone),
		("@here", DiscordMentionKind.Here)
	];

	private static readonly (string Prefix, string Variant)[] _headings =
	[
		("### ", "heading-3"),
		("## ", "heading-2"),
		("# ", "heading-1"),
		("-# ", SubtextVariant)
	];

	private readonly IReadOnlyDictionary<string, string> _names;

	public DiscordMarkdown(IReadOnlyDictionary<string, string> names)
	{
		_names = names;
	}

	public List<DiscordMention> Mentions { get; } = [];

	public IReadOnlyList<DiscordMarkdownBlock> RenderBlocks(string markdown)
	{
		var blocks = new List<DiscordMarkdownBlock>();
		var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		var paragraph = new List<string>();
		var quote = new List<string>();

		void FlushParagraph()
		{
			if (paragraph.Count > 0)
			{
				blocks.Add(Block(null, string.Join('\n', paragraph)));
				paragraph.Clear();
			}
		}

		void FlushQuote()
		{
			if (quote.Count > 0)
			{
				blocks.Add(Block(QuoteVariant, string.Join('\n', quote)));
				quote.Clear();
			}
		}

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];

			if (line.StartsWith(Fence, StringComparison.Ordinal)
				&& TryReadCodeBlock(lines, i, out var code, out var lastLine))
			{
				FlushParagraph();
				FlushQuote();
				blocks.Add(CodeBlock(code));
				i = lastLine;
				continue;
			}

			if (line.StartsWith(">>> ", StringComparison.Ordinal))
			{
				FlushParagraph();
				quote.Add(line[4..]);
				quote.AddRange(lines[(i + 1)..]);
				break;
			}

			if (line.StartsWith("> ", StringComparison.Ordinal) || line == ">")
			{
				FlushParagraph();
				quote.Add(line.Length > 1 ? line[2..] : string.Empty);
				continue;
			}

			FlushQuote();

			var heading = HeadingVariant(line, out var headingText);
			if (heading is not null)
			{
				FlushParagraph();
				blocks.Add(Block(heading, headingText));
				continue;
			}

			paragraph.Add(line);
		}

		FlushParagraph();
		FlushQuote();
		return blocks;
	}

	public IReadOnlyList<UiTextSpan> RenderInline(string text, bool bold = false, string? color = null)
	{
		var spans = new List<UiTextSpan>();
		Inline(text, new Style { Bold = bold, Color = color }, spans);
		return spans;
	}

	public static string PlainText(IReadOnlyList<UiTextSpan> spans)
	{
		return string.Concat(spans.Select(span => span.Text));
	}

	private static string? HeadingVariant(string line, out string text)
	{
		foreach (var (prefix, variant) in _headings)
		{
			if (line.StartsWith(prefix, StringComparison.Ordinal) && line[prefix.Length..].Trim().Length > 0)
			{
				text = line[prefix.Length..];
				return variant;
			}
		}

		text = line;
		return null;
	}

	private static bool TryReadCodeBlock(string[] lines, int start, out string code, out int lastLine)
	{
		var first = lines[start][Fence.Length..];
		var sameLineEnd = first.IndexOf(Fence, StringComparison.Ordinal);
		if (sameLineEnd >= 0)
		{
			code = first[..sameLineEnd];
			lastLine = start;
			return code.Length > 0;
		}

		var body = new List<string>();
		if (first.Trim().Contains(' ', StringComparison.Ordinal))
		{
			body.Add(first);
		}

		for (var i = start + 1; i < lines.Length; i++)
		{
			var end = lines[i].IndexOf(Fence, StringComparison.Ordinal);
			if (end >= 0)
			{
				body.Add(lines[i][..end]);
				code = string.Join('\n', body).Trim('\n');
				lastLine = i;
				return true;
			}

			body.Add(lines[i]);
		}

		code = string.Empty;
		lastLine = start;
		return false;
	}

	private static DiscordMarkdownBlock CodeBlock(string code)
	{
		IReadOnlyList<UiTextSpan> spans =
		[
			new()
				{ Text = code, Monospace = true }
		];
		return new DiscordMarkdownBlock(CodeBlockVariant, spans, code);
	}

	private DiscordMarkdownBlock Block(string? variant, string text)
	{
		var spans = new List<UiTextSpan>();
		Inline(text, variant is "heading-1" or "heading-2" or "heading-3" ? new Style { Bold = true } : default, spans);
		return new DiscordMarkdownBlock(variant, spans, PlainText(spans));
	}

	private void Inline(string text, Style style, List<UiTextSpan> spans)
	{
		var buffer = new StringBuilder();

		void Flush()
		{
			if (buffer.Length > 0)
			{
				spans.Add(style.ToSpan(buffer.ToString()));
				buffer.Clear();
			}
		}

		var i = 0;
		while (i < text.Length)
		{
			var c = text[i];

			if (c == '\\'
				&& i + 1 < text.Length
				&& !char.IsLetterOrDigit(text[i + 1])
				&& !char.IsWhiteSpace(text[i + 1]))
			{
				buffer.Append(text[i + 1]);
				i += 2;
				continue;
			}

			if (c == '`')
			{
				var end = text.IndexOf('`', i + 1);
				if (end > i + 1)
				{
					Flush();
					spans.Add((style with { Code = true }).ToSpan(text[(i + 1)..end]));
					i = end + 1;
					continue;
				}
			}

			if (TryMention(text, i, style, out var mentionSpan, out var mentionLength)
				|| TryLink(text, i, style, out mentionSpan, out mentionLength))
			{
				Flush();
				spans.Add(mentionSpan);
				i += mentionLength;
				continue;
			}

			if (TryDelimited(text, i, "**", out var inner, out var length)
				&& Emit(inner, style with { Bold = true }))
			{
				i += length;
				continue;
			}

			if (TryDelimited(text, i, "__", out inner, out length) && Emit(inner, style with { Underline = true }))
			{
				i += length;
				continue;
			}

			if (TryDelimited(text, i, "~~", out inner, out length) && Emit(inner, style with { Strikethrough = true }))
			{
				i += length;
				continue;
			}

			if (TryDelimited(text, i, "||", out inner, out length) && Emit(inner, style with { Spoiler = true }))
			{
				i += length;
				continue;
			}

			if (c == '*'
				&& TryDelimited(text, i, "*", out inner, out length)
				&& !inner.StartsWith(' ')
				&& Emit(inner, style with { Italic = true }))
			{
				i += length;
				continue;
			}

			if (c == '_'
				&& (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
				&& TryDelimited(text, i, "_", out inner, out length)
				&& (i + length >= text.Length || !char.IsLetterOrDigit(text[i + length]))
				&& Emit(inner, style with { Italic = true }))
			{
				i += length;
				continue;
			}

			buffer.Append(c);
			i++;
		}

		Flush();

		bool Emit(string content, Style nested)
		{
			Flush();
			Inline(content, nested, spans);
			return true;
		}
	}

	private static bool TryDelimited(string text, int index, string delimiter, out string inner, out int length)
	{
		inner = string.Empty;
		length = 0;
		if (string.CompareOrdinal(text, index, delimiter, 0, delimiter.Length) != 0)
		{
			return false;
		}

		var start = index + delimiter.Length;
		var end = text.IndexOf(delimiter, start, StringComparison.Ordinal);
		if (end <= start)
		{
			return false;
		}

		// A run such as *** closes the outer pair last, so the inner marker keeps its own closing character.
		while (delimiter.Length > 1
			&& end + delimiter.Length < text.Length
			&& text[end + delimiter.Length] == delimiter[0])
		{
			end++;
		}

		inner = text[start..end];
		length = end + delimiter.Length - index;
		return true;
	}

	private bool TryMention(string text, int index, Style style, out UiTextSpan span, out int length)
	{
		span = null!;
		length = 0;

		var match = MentionRegex().Match(text, index);
		if (match.Success && match.Index == index)
		{
			var id = match.Groups["id"].Value;
			var kind = match.Groups["kind"].Value switch
			{
				"@&" => DiscordMentionKind.Role,
				"#" => DiscordMentionKind.Channel,
				_ => DiscordMentionKind.User
			};
			Mentions.Add(new DiscordMention(kind, id));

			var label = _names.TryGetValue(id, out var name) ? name : id;
			span = Pill(style, (kind == DiscordMentionKind.Channel ? "#" : "@") + label);
			length = match.Length;
			return true;
		}

		foreach (var (literal, kind) in _broadcastMentions)
		{
			if (string.CompareOrdinal(text, index, literal, 0, literal.Length) == 0)
			{
				Mentions.Add(new DiscordMention(kind, literal));
				span = Pill(style, literal);
				length = literal.Length;
				return true;
			}
		}

		return false;
	}

	private static bool TryLink(string text, int index, Style style, out UiTextSpan span, out int length)
	{
		span = null!;
		length = 0;

		var masked = MaskedLinkRegex().Match(text, index);
		if (masked.Success && masked.Index == index)
		{
			span = Link(style, masked.Groups["label"].Value);
			length = masked.Length;
			return true;
		}

		var bare = BareLinkRegex().Match(text, index);
		if (bare.Success && bare.Index == index)
		{
			span = Link(style, bare.Groups["url"].Value);
			length = bare.Length;
			return true;
		}

		return false;
	}

	private static UiTextSpan Link(Style style, string text)
	{
		return (style with { Color = DiscordPreviewPalette.Link, Underline = true }).ToSpan(text);
	}

	private static UiTextSpan Pill(Style style, string text)
	{
		var pill = style with
		{
			Color = DiscordPreviewPalette.MentionText, Background = DiscordPreviewPalette.MentionBackground
		};

		return pill.ToSpan(text);
	}

	[GeneratedRegex("""\G<(?<kind>@!?|@&|#)(?<id>\d{1,20})>""", RegexOptions.CultureInvariant)]
	private static partial Regex MentionRegex();

	[GeneratedRegex("""\G\[(?<label>[^\]\n]+)\]\(<?(?<url>https?://[^\s)>]+)>?\)""", RegexOptions.CultureInvariant)]
	private static partial Regex MaskedLinkRegex();

	[GeneratedRegex("""\G<?(?<url>https?://[^\s<>]+[^\s<>.,:;"')\]])>?""", RegexOptions.CultureInvariant)]
	private static partial Regex BareLinkRegex();

	private readonly record struct Style
	{
		public bool Bold { get; init; }

		public bool Italic { get; init; }

		public bool Underline { get; init; }

		public bool Strikethrough { get; init; }

		public bool Code { get; init; }

		public bool Spoiler { get; init; }

		public string? Color { get; init; }

		public string? Background { get; init; }

		public UiTextSpan ToSpan(string text)
		{
			return new UiTextSpan
			{
				Text = text,
				Color = Color,
				Weight = Bold ? UiComponentTextWeights.Bold : null,
				Italic = Italic ? true : null,
				Underline = Underline ? true : null,
				Strikethrough = Strikethrough ? true : null,
				Monospace = Code ? true : null,
				Background = Code
					? DiscordPreviewPalette.CodeBackground
					: Spoiler
						? DiscordPreviewPalette.SpoilerBackground
						: Background
			};
		}
	}
}
