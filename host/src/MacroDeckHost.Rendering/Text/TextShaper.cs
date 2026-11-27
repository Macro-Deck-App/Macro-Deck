using System.Globalization;
using HarfBuzzSharp;
using Buffer = HarfBuzzSharp.Buffer;

namespace MacroDeckHost.Rendering.Text;

internal sealed record ShapedRun(
	FontFace Face,
	uint[] Glyphs,
	double[] Advances,
	double[] OffsetsX,
	double[] OffsetsY,
	int[] Clusters,
	int Start,
	int Length)
{
	public double Width => Advances.Sum();
}

internal sealed record ShapedText(IReadOnlyList<ShapedRun> Runs, double Width);

internal readonly record struct FontSpec(string Family, double Size, bool Tabular);

internal sealed class TextShaper
{
	private static readonly Feature[] _defaults = [];

	private static readonly Feature[] _tabular = [Feature.Parse("tnum")];

	private readonly IRenderFonts _fonts;

	private readonly string _culture;

	private readonly Dictionary<(string, FontSpec), ShapedText> _cache = [];

	public TextShaper(IRenderFonts fonts, string culture)
	{
		_fonts = fonts;
		_culture = culture;
	}

	public FontFace PrimaryFace(string family)
	{
		var faces = _fonts.Faces(family, _culture);
		return faces.Count > 0 ? faces[0].Opened : throw new InvalidOperationException("no font face is available");
	}

	public double Width(string text, FontSpec spec)
	{
		return Shape(text, spec).Width;
	}

	public ShapedText Shape(string text, FontSpec spec)
	{
		if (_cache.TryGetValue((text, spec), out var cached))
		{
			return cached;
		}

		var runs = new List<ShapedRun>();
		double width = 0;
		foreach (var (face, start, length) in Segment(text, spec.Family))
		{
			var run = ShapeRun(face, text, start, length, spec);
			runs.Add(run);
			width += run.Width;
		}

		var shaped = new ShapedText(runs, width);
		_cache[(text, spec)] = shaped;
		return shaped;
	}

	public double ZeroWidth(FontSpec spec)
	{
		var face = PrimaryFace(spec.Family);
		if (!face.Covers('0'))
		{
			return spec.Size / 2;
		}

		face.HarfBuzzFont.TryGetGlyph('0', out var glyph);
		return face.HarfBuzzFont.GetHorizontalGlyphAdvance(glyph) * spec.Size / face.UnitsPerEm;
	}

	private List<(FontFace Face, int Start, int Length)> Segment(string text, string family)
	{
		var faces = _fonts.Faces(family, _culture);
		var segments = new List<(FontFace Face, int Start, int Length)>();
		FontFace? current = null;
		var start = 0;
		var index = 0;
		while (index < text.Length)
		{
			var codepoint = char.ConvertToUtf32(text, index);
			var width = char.IsSurrogatePair(text, index) ? 2 : 1;
			var face = Covering(faces, codepoint, current);
			if (current is null)
			{
				current = face;
			}
			else if (!ReferenceEquals(face, current))
			{
				segments.Add((current, start, index - start));
				current = face;
				start = index;
			}

			index += width;
		}

		if (current is not null)
		{
			segments.Add((current, start, text.Length - start));
		}

		return segments;
	}

	private FontFace Covering(IReadOnlyList<RenderFontFace> faces, int codepoint, FontFace? current)
	{
		var category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
		var neutral = category is UnicodeCategory.SpaceSeparator or UnicodeCategory.Control or UnicodeCategory.Format
			|| codepoint == 0x200B;
		if (current is not null && (neutral || current.Covers(codepoint)))
		{
			return current;
		}

		foreach (var face in faces)
		{
			if (face.Opened.Covers(codepoint))
			{
				return face.Opened;
			}
		}

		if (!neutral && _fonts.Fallback(codepoint, _culture) is { } fallback && fallback.Opened.Covers(codepoint))
		{
			return fallback.Opened;
		}

		return current ?? faces[0].Opened;
	}

	private static ShapedRun ShapeRun(FontFace face, string text, int start, int length, FontSpec spec)
	{
		using var buffer = new Buffer();
		buffer.AddUtf16(text, start, length);
		buffer.GuessSegmentProperties();
		face.HarfBuzzFont.Shape(buffer, spec.Tabular ? _tabular : _defaults);
		var infos = buffer.GlyphInfos;
		var positions = buffer.GlyphPositions;
		var scale = spec.Size / face.UnitsPerEm;
		var glyphs = new uint[infos.Length];
		var advances = new double[infos.Length];
		var offsetsX = new double[infos.Length];
		var offsetsY = new double[infos.Length];
		var clusters = new int[infos.Length];
		for (var index = 0; index < infos.Length; index++)
		{
			glyphs[index] = infos[index].Codepoint;
			advances[index] = positions[index].XAdvance * scale;
			offsetsX[index] = positions[index].XOffset * scale;
			offsetsY[index] = positions[index].YOffset * scale;
			clusters[index] = (int)infos[index].Cluster;
		}

		return new ShapedRun(face, glyphs, advances, offsetsX, offsetsY, clusters, start, length);
	}
}
