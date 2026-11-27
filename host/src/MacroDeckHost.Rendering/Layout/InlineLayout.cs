using System.Globalization;
using MacroDeckHost.Rendering.Dom;
using MacroDeckHost.Rendering.Text;

namespace MacroDeckHost.Rendering.Layout;

internal sealed record InlinePiece(string Text, Element Owner, FontSpec Font, double PaddingStart, double PaddingEnd);

internal sealed record TextFragment(string Text, Element Owner, FontSpec Font, double X, double Width);

internal sealed class TextLine
{
	public List<TextFragment> Fragments { get; } = [];

	public LayoutUnit Width { get; set; }

	public LayoutUnit Top { get; set; }

	public LayoutUnit Height { get; set; }

	public LayoutUnit Offset { get; set; }

	public LayoutUnit Baseline { get; set; }
}

internal sealed class InlineLayout
{
	private const float FontCachePrecision = 100f;

	private readonly TextShaper _shaper;

	public InlineLayout(TextShaper shaper)
	{
		_shaper = shaper;
	}

	public static bool HasInlineContent(Element element)
	{
		foreach (var child in element.Children)
		{
			if (child.IsText || child.Computed.Display is "inline" or "inline-block")
			{
				return true;
			}
		}

		return false;
	}

	public static FontSpec FontOf(ComputedStyle style)
	{
		var family = style.Get("font-family").Contains("mono", StringComparison.OrdinalIgnoreCase) ? "mono" : "sans";
		var tabular = style.Get("font-variant-numeric").Contains("tabular-nums", StringComparison.Ordinal)
			|| style.Get("font-feature-settings").Contains("tnum", StringComparison.Ordinal);
		// Chrome shapes at the font size held as a layout unit.
		return new FontSpec(family,
			Math.Floor(EffectiveFontSize(style.FontSize) * LayoutUnit.Denominator) / LayoutUnit.Denominator,
			tabular);
	}

	// Blink's FontDescription::EffectiveFontSize: the float computed size floored to the font cache's
	// precision of 1/100 px, so 16.8px (16.7999992 as a float) is set at 16.79.
	public static double EffectiveFontSize(double fontSize)
	{
		return MathF.Floor((float)fontSize * FontCachePrecision) / FontCachePrecision;
	}

	// Chrome rounds a face's ascent and descent to whole pixels before it adds the half-leading.
	public (LayoutUnit Ascent, LayoutUnit Descent) FontMetrics(ComputedStyle style)
	{
		var face = _shaper.PrimaryFace(FontOf(style).Family);
		var scale = style.FontSize / face.UnitsPerEm;
		return (LayoutUnit.FromInt((int)Math.Round(face.Ascender * scale, MidpointRounding.AwayFromZero)),
			LayoutUnit.FromInt((int)Math.Round(face.Descender * scale, MidpointRounding.AwayFromZero)));
	}

	public static LayoutUnit LineHeight(ComputedStyle style)
	{
		return LayoutUnit.FromDoubleRound(style.LineHeightPx);
	}

	public static List<InlinePiece> Pieces(Element element)
	{
		var pieces = new List<InlinePiece>();
		Collect(element, element, pieces);
		return pieces;
	}

	private static void Collect(Element root, Element element, List<InlinePiece> pieces)
	{
		foreach (var child in element.Children)
		{
			if (child.IsText)
			{
				if (!string.IsNullOrEmpty(child.Text))
				{
					pieces.Add(new InlinePiece(child.Text, element, FontOf(element.Computed), 0, 0));
				}

				continue;
			}

			if (child.Computed.Display == "none")
			{
				continue;
			}

			var start = pieces.Count;
			Collect(root, child, pieces);
			if (pieces.Count == start)
			{
				continue;
			}

			var style = child.Computed;
			var paddingStart = style.Px("padding-left") + style.Px("border-left-width");
			var paddingEnd = style.Px("padding-right") + style.Px("border-right-width");
			if (paddingStart != 0 || paddingEnd != 0)
			{
				pieces[start] = pieces[start] with { PaddingStart = pieces[start].PaddingStart + paddingStart };
				pieces[^1] = pieces[^1] with { PaddingEnd = pieces[^1].PaddingEnd + paddingEnd };
			}
		}
	}

	public LayoutUnit MaxContent(Element element)
	{
		var lines = BuildLines(element, null);
		var widest = LayoutUnit.Zero;
		foreach (var line in lines)
		{
			widest = LayoutUnit.MaxOf(widest, line.Width);
		}

		return widest;
	}

	public LayoutUnit MinContent(Element element)
	{
		var style = element.Computed;
		var wraps = style.WhiteSpace is "normal" or "pre-wrap" or "pre-line" or "break-spaces";
		if (!wraps)
		{
			return MaxContent(element);
		}

		var anywhere = style.Get("overflow-wrap") == "anywhere" || style.Get("word-break") == "break-all";
		var widest = LayoutUnit.Zero;
		foreach (var piece in Normalize(Pieces(element), style.WhiteSpace))
		{
			foreach (var word in Words(piece.Text, anywhere))
			{
				var trimmed = word.TrimEnd(' ');
				if (trimmed.Length == 0)
				{
					continue;
				}

				widest = LayoutUnit.MaxOf(widest,
					Measure(trimmed, piece.Font) + LayoutUnit.FromDouble(piece.PaddingStart + piece.PaddingEnd));
			}
		}

		return widest;
	}

	public LayoutUnit Measure(string text, FontSpec font)
	{
		return LayoutUnit.FromDoubleCeil(_shaper.Width(text, font));
	}

	public List<TextLine> BuildLines(Element element, LayoutUnit? available)
	{
		var style = element.Computed;
		var whiteSpace = style.WhiteSpace;
		var collapses = whiteSpace is "normal" or "nowrap" or "pre-line";
		var wraps = available is not null && whiteSpace is "normal" or "pre-wrap" or "pre-line" or "break-spaces";
		var anywhere = style.Get("overflow-wrap") == "anywhere" || style.Get("word-break") == "break-all";
		var builder = new LineBuilder(this);
		foreach (var piece in Normalize(Pieces(element), whiteSpace))
		{
			var segments = piece.Text.Split('\n');
			for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
			{
				if (segmentIndex > 0)
				{
					builder.Break(collapses);
				}

				var segment = segments[segmentIndex];
				var padStart = segmentIndex == 0 ? piece.PaddingStart : 0;
				var padEnd = segmentIndex == segments.Length - 1 ? piece.PaddingEnd : 0;
				builder.Open(piece, padStart);
				foreach (var word in wraps ? Words(segment, false) : [segment])
				{
					var placed = builder.AtLineStart && collapses ? word.TrimStart(' ') : word;
					if (placed.Length == 0)
					{
						continue;
					}

					var visible = placed.TrimEnd(' ');
					if (wraps
						&& !builder.AtLineStart
						&& visible.Length > 0
						&& builder.WidthWith(visible) > available!.Value)
					{
						builder.Break(collapses);
						builder.Open(piece, 0);
						placed = collapses ? placed.TrimStart(' ') : placed;
						if (placed.Length == 0)
						{
							continue;
						}
					}

					// overflow-wrap: anywhere breaks a word only when it cannot fit a line of its own.
					while (wraps
						&& anywhere
						&& builder.AtLineStart
						&& placed.TrimEnd(' ').Length > 1
						&& builder.WidthWith(placed.TrimEnd(' ')) > available!.Value)
					{
						var fits = 1;
						while (fits < placed.Length && builder.WidthWith(placed[..(fits + 1)]) <= available.Value)
						{
							fits++;
						}

						builder.Add(placed[..fits]);
						builder.Break(collapses);
						builder.Open(piece, 0);
						placed = placed[fits..];
					}

					builder.Add(placed);
				}

				builder.Close(padEnd);
			}
		}

		builder.Break(collapses);
		return builder.Lines;
	}

	private sealed class LineBuilder
	{
		private readonly InlineLayout _layout;

		private TextLine _line = new();

		private InlinePiece? _piece;

		private string _text = "";

		private LayoutUnit _settled = LayoutUnit.Zero;

		private LayoutUnit _pieceStart = LayoutUnit.Zero;

		public LineBuilder(InlineLayout layout)
		{
			_layout = layout;
		}

		public List<TextLine> Lines { get; } = [];

		public bool AtLineStart { get; private set; } = true;

		public void Open(InlinePiece piece, double paddingStart)
		{
			Flush();
			_piece = piece;
			_settled += LayoutUnit.FromDouble(paddingStart);
			_pieceStart = _settled;
			_text = "";
		}

		public LayoutUnit WidthWith(string visible)
		{
			return _pieceStart + _layout.Measure(_text + visible, _piece!.Font);
		}

		public void Add(string text)
		{
			_text += text;
			AtLineStart = false;
		}

		public void Close(double paddingEnd)
		{
			Flush();
			_settled += LayoutUnit.FromDouble(paddingEnd);
			_piece = null;
		}

		// Trailing spaces hang: they are kept in the text but never count toward the line's width.
		public void Break(bool collapses)
		{
			var piece = _piece;
			_text = _text.TrimEnd(' ');
			Flush();
			if (_line.Fragments.Count > 0 && collapses)
			{
				var last = _line.Fragments[^1];
				var trimmed = last.Text.TrimEnd(' ');
				if (trimmed.Length != last.Text.Length)
				{
					var width = trimmed.Length == 0 ? LayoutUnit.Zero : _layout.Measure(trimmed, last.Font);
					_settled -= LayoutUnit.FromDouble(last.Width) - width;
					_line.Fragments[^1] = last with { Text = trimmed, Width = width.Value };
				}
			}

			_line.Width = _settled;
			Lines.Add(_line);
			_line = new TextLine();
			_settled = LayoutUnit.Zero;
			_pieceStart = LayoutUnit.Zero;
			AtLineStart = true;
			_piece = piece;
			_text = "";
		}

		private void Flush()
		{
			if (_piece is null || _text.Length == 0)
			{
				_text = "";
				return;
			}

			var width = _layout.Measure(_text, _piece.Font);
			_line.Fragments.Add(new TextFragment(_text, _piece.Owner, _piece.Font, _pieceStart.Value, width.Value));
			_settled = _pieceStart + width;
			_pieceStart = _settled;
			_text = "";
		}
	}

	private static List<InlinePiece> Normalize(List<InlinePiece> pieces, string whiteSpace)
	{
		if (whiteSpace is "pre" or "pre-wrap" or "break-spaces")
		{
			return pieces;
		}

		var result = new List<InlinePiece>();
		var previousSpace = true;
		foreach (var piece in pieces)
		{
			var builder = new System.Text.StringBuilder();
			foreach (var character in piece.Text)
			{
				var isNewline = character == '\n';
				var space = character is ' ' or '\t' or '\r' || isNewline && whiteSpace != "pre-line";
				if (isNewline && whiteSpace == "pre-line")
				{
					builder.Append('\n');
					previousSpace = true;
					continue;
				}

				if (space)
				{
					if (!previousSpace)
					{
						builder.Append(' ');
					}

					previousSpace = true;
					continue;
				}

				builder.Append(character);
				previousSpace = false;
			}

			result.Add(piece with { Text = builder.ToString() });
		}

		return result;
	}

	private static List<string> Words(string text, bool anywhere)
	{
		var words = new List<string>();
		var start = 0;
		var index = 0;
		while (index < text.Length)
		{
			var width = char.IsSurrogatePair(text, index) ? 2 : 1;
			var codepoint = char.ConvertToUtf32(text, index);
			var next = index + width;
			if (text[index] == '-' && next < text.Length && !char.IsDigit(text[next]) && index > start)
			{
				words.Add(text[start..next]);
				start = next;
			}
			else if (text[index] == ' ')
			{
				while (next < text.Length && text[next] == ' ')
				{
					next++;
				}

				words.Add(text[start..next]);
				start = next;
			}
			else if (anywhere
				|| IsIdeographic(codepoint)
				|| next < text.Length && IsIdeographic(char.ConvertToUtf32(text, next)))
			{
				var end = next;
				while (end < text.Length && text[end] == ' ')
				{
					end++;
				}

				if (anywhere || end > next || IsIdeographic(codepoint))
				{
					words.Add(text[start..end]);
					start = end;
					next = end;
				}
			}

			index = next;
		}

		if (start < text.Length)
		{
			words.Add(text[start..]);
		}

		return words;
	}

	private static bool IsIdeographic(int codepoint)
	{
		return codepoint is >= 0x2E80 and <= 0x9FFF
				or >= 0xAC00 and <= 0xD7AF
				or >= 0xF900 and <= 0xFAFF
				or >= 0xFF00 and <= 0xFFEF
				or >= 0x20000 and <= 0x2FA1F
			|| CharUnicodeInfo.GetUnicodeCategory(codepoint) == UnicodeCategory.OtherLetter && codepoint >= 0x3040;
	}
}
