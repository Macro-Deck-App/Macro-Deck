using System.Collections.Concurrent;
using HarfBuzzSharp;
using SkiaSharp;

namespace MacroDeckHost.Rendering.Text;

internal sealed class FontFace : IDisposable
{
	private readonly Blob _blob;

	private readonly Face _face;

	private readonly Font _font;

	// Holds Skia's own reference to the typeface: the font manager hands every caller the same managed
	// wrapper, so a wrapper another caller disposes must never be the one this face draws with.
	private readonly SKFont _drawing;

	private readonly Lock _drawingGate = new();

	private readonly ConcurrentDictionary<int, bool> _coverage = new();

	private FontFace(string name, SKData data, int collectionIndex, SKTypeface typeface)
	{
		Name = name;
		CollectionIndex = collectionIndex;
		_drawing = new SKFont(typeface) { Subpixel = true, Edging = SKFontEdging.Antialias };
		_blob = new Blob(data.Data, (int)data.Size, MemoryMode.ReadOnly, data.Dispose);
		_blob.MakeImmutable();
		_face = new Face(_blob, collectionIndex);
		_face.MakeImmutable();
		UnitsPerEm = _face.UnitsPerEm;
		_font = new Font(_face);
		_font.SetScale(UnitsPerEm, UnitsPerEm);
		_font.SetFunctionsOpenType();
		if (_font.TryGetHorizontalFontExtents(out var extents))
		{
			Ascender = extents.Ascender;
			Descender = -extents.Descender;
			LineGap = extents.LineGap;
		}
	}

	public string Name { get; }

	public int CollectionIndex { get; }

	public int UnitsPerEm { get; }

	public int Ascender { get; }

	public int Descender { get; }

	public int LineGap { get; }

	internal Font HarfBuzzFont => _font;

	internal Face HarfBuzzFace => _face;

	internal SKTypeface DrawingTypeface
	{
		get
		{
			lock (_drawingGate)
			{
				return _drawing.Typeface;
			}
		}
	}

	public static FontFace? FromData(string name, byte[] data)
	{
		var shared = SKData.CreateCopy(data);
		using var typeface = SKTypeface.FromData(shared);
		if (typeface is null)
		{
			shared.Dispose();
			return null;
		}

		return Matched(new FontFace(name, shared, 0, typeface));
	}

	public static FontFace? FromTypeface(SKTypeface typeface)
	{
		using var stream = typeface.OpenStream(out var collectionIndex);
		if (stream is null || SKData.Create(stream) is not { } data)
		{
			return null;
		}

		return Matched(new FontFace(typeface.FamilyName, data, collectionIndex, typeface));
	}

	public bool Covers(int codepoint)
	{
		return _coverage.GetOrAdd(codepoint, point => _font.TryGetGlyph((uint)point, out var glyph) && glyph != 0);
	}

	public SKPositionedRunBuffer AllocateRun(SKTextBlobBuilder builder, float size, int count)
	{
		lock (_drawingGate)
		{
			_drawing.Size = size;
			return builder.AllocatePositionedRun(_drawing, count);
		}
	}

	public void Dispose()
	{
		_font.Dispose();
		_face.Dispose();
		_blob.Dispose();
		_drawing.Dispose();
	}

	private static FontFace? Matched(FontFace face)
	{
		int drawn;
		lock (face._drawingGate)
		{
			drawn = face._drawing.Typeface?.GlyphCount ?? 0;
		}

		if (face._face.GlyphCount > 0 && face._face.GlyphCount == drawn)
		{
			return face;
		}

		face.Dispose();
		return null;
	}
}
