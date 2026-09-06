using System.Globalization;
using MacroDeckHost.Application.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Rendering;

public sealed class SkiaFontCatalog : IFontCatalog
{
	private const int RegularWeight = (int)SKFontStyleWeight.Normal;

	private readonly Lazy<Catalog> _catalog = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

	public IReadOnlyList<FontFaceInfo> GetFaces() => _catalog.Value.Faces;

	public byte[]? GetFaceFile(string faceId)
	{
		if (string.IsNullOrWhiteSpace(faceId) || !_catalog.Value.Sources.TryGetValue(faceId, out var source))
		{
			return null;
		}

		using var styles = SKFontManager.Default.GetFontStyles(source.Family);
		if (source.StyleIndex >= styles.Count)
		{
			return null;
		}

		using var style = styles[source.StyleIndex];
		if (style.Weight != source.Weight || style.Width != source.Width || style.Slant != source.Slant)
		{
			return null;
		}

		using var typeface = styles.CreateTypeface(source.StyleIndex);
		return typeface is null ? null : SfntFaceExtractor.Extract(typeface);
	}

	private static Catalog Load()
	{
		var manager = SKFontManager.Default;
		var faces = new List<FontFaceInfo>();
		var sources = new Dictionary<string, FaceSource>(StringComparer.Ordinal);

		var families = manager.FontFamilies
			.Where(family => !string.IsNullOrWhiteSpace(family))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(family => family, StringComparer.OrdinalIgnoreCase);

		foreach (var family in families)
		{
			using var styles = manager.GetFontStyles(family);
			for (var index = 0; index < styles.Count; index++)
			{
				using var style = styles[index];
				using var typeface = styles.CreateTypeface(index);

				var faceId = ReserveFaceId(sources, family, style);
				sources[faceId] = new FaceSource(family, index, style.Weight, style.Width, style.Slant);
				faces.Add(new FontFaceInfo(faceId,
					family,
					style.Weight,
					style.Width,
					SlantId(style.Slant),
					StyleName(style),
					typeface is not null && SfntFaceExtractor.CanExtract(typeface)));
			}
		}

		return new Catalog(faces, sources);
	}

	private static string ReserveFaceId(Dictionary<string, FaceSource> taken, string family, SKFontStyle style)
	{
		var baseId = FontFaceIdentity.Build(family, style.Weight, style.Width, SlantId(style.Slant));
		if (!taken.ContainsKey(baseId))
		{
			return baseId;
		}

		for (var suffix = 2;; suffix++)
		{
			var candidate = $"{baseId}-{suffix}";
			if (!taken.ContainsKey(candidate))
			{
				return candidate;
			}
		}
	}

	private static string SlantId(SKFontStyleSlant slant) => slant switch
	{
		SKFontStyleSlant.Italic => FontFaceIdentity.ItalicSlant,
		SKFontStyleSlant.Oblique => FontFaceIdentity.ObliqueSlant,
		_ => FontFaceIdentity.UprightSlant
	};

	private static string StyleName(SKFontStyle style)
	{
		var parts = new List<string>(3);

		if (WidthName(style.Width) is { } width)
		{
			parts.Add(width);
		}

		if (style.Weight != RegularWeight)
		{
			parts.Add(WeightName(style.Weight));
		}

		if (style.Slant != SKFontStyleSlant.Upright)
		{
			parts.Add(style.Slant == SKFontStyleSlant.Oblique ? "Oblique" : "Italic");
		}

		return parts.Count == 0 ? "Regular" : string.Join(' ', parts);
	}

	private static string? WidthName(int width) => width switch
	{
		1 => "UltraCondensed",
		2 => "ExtraCondensed",
		3 => "Condensed",
		4 => "SemiCondensed",
		6 => "SemiExpanded",
		7 => "Expanded",
		8 => "ExtraExpanded",
		9 => "UltraExpanded",
		_ => null
	};

	private static string WeightName(int weight) => weight switch
	{
		100 => "Thin",
		200 => "ExtraLight",
		300 => "Light",
		RegularWeight => "Regular",
		500 => "Medium",
		600 => "SemiBold",
		700 => "Bold",
		800 => "ExtraBold",
		900 => "Black",
		1000 => "ExtraBlack",
		_ => weight.ToString(CultureInfo.InvariantCulture)
	};

	private sealed record FaceSource(string Family, int StyleIndex, int Weight, int Width, SKFontStyleSlant Slant);

	private sealed record Catalog(IReadOnlyList<FontFaceInfo> Faces, IReadOnlyDictionary<string, FaceSource> Sources);
}
