using System.Buffers.Binary;
using System.Globalization;
using MacroDeckHost.Application.Rendering;
using SkiaSharp;

namespace MacroDeckHost.Infrastructure.Rendering;

public sealed class SkiaFontCatalog : IFontCatalog
{
	private const int RegularWeight = (int)SKFontStyleWeight.Normal;
	private const uint CollectionSignature = 0x74746366;
	private const int MaxDirectoryDepth = 8;

	private static readonly HashSet<string> FontFileExtensions =
		new([".ttf", ".otf", ".ttc", ".otc"], StringComparer.OrdinalIgnoreCase);

	private readonly Lazy<Catalog> _catalog;

	public SkiaFontCatalog()
		: this(LinuxFontDirectories.Resolve())
	{
	}

	internal SkiaFontCatalog(IReadOnlyList<string> additionalFontDirectories)
		: this(additionalFontDirectories, EnumerateSystemFaces)
	{
	}

	internal SkiaFontCatalog(
		IReadOnlyList<string> additionalFontDirectories,
		Func<IEnumerable<SystemFaceEntry>> systemFaces)
	{
		_catalog = new Lazy<Catalog>(() => Load(additionalFontDirectories, systemFaces()),
			LazyThreadSafetyMode.ExecutionAndPublication);
	}

	public IReadOnlyList<FontFaceInfo> GetFaces() => _catalog.Value.Faces;

	public string ResolveFaceId(string faceId) =>
		_catalog.Value.Aliases.TryGetValue(faceId, out var canonical) ? canonical : faceId;

	public byte[]? GetFaceFile(string faceId)
	{
		if (string.IsNullOrWhiteSpace(faceId) || !_catalog.Value.Sources.TryGetValue(faceId, out var source))
		{
			return null;
		}

		if (source.FilePath is not null)
		{
			using var fileTypeface = OpenFace(source.FilePath, source.StyleIndex);
			if (fileTypeface is null ||
				!string.Equals(fileTypeface.FamilyName, source.Family, StringComparison.Ordinal) ||
				fileTypeface.FontWeight != source.Weight ||
				fileTypeface.FontWidth != source.Width ||
				fileTypeface.FontSlant != source.Slant)
			{
				return null;
			}

			return SfntFaceExtractor.Extract(fileTypeface);
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

	private static Catalog Load(IReadOnlyList<string> additionalFontDirectories, IEnumerable<SystemFaceEntry> systemFaces)
	{
		var faces = new List<FontFaceInfo>();
		var sources = new Dictionary<string, FaceSource>(StringComparer.Ordinal);
		var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

		// Fingerprints reach back into the family's native style set, so nothing survives a family change.
		var listed = new Dictionary<FaceKey, List<(string FaceId, Lazy<string?> Fingerprint)>>();
		string? currentFamily = null;

		foreach (var entry in systemFaces)
		{
			if (!string.Equals(entry.Family, currentFamily, StringComparison.Ordinal))
			{
				currentFamily = entry.Family;
				listed.Clear();
			}

			using var style = new SKFontStyle(entry.Weight, entry.Width, entry.Slant);
			var faceId = ReserveFaceId(sources, entry.Family, style);
			sources[faceId] = new FaceSource(entry.Family, entry.StyleIndex, entry.Weight, entry.Width, entry.Slant);

			var key = new FaceKey(entry.Family.ToUpperInvariant(), entry.Weight, entry.Width, SlantId(entry.Slant));
			var fingerprint = new Lazy<string?>(entry.Fingerprint);
			if (listed.TryGetValue(key, out var sameStyle))
			{
				var hash = fingerprint.Value;
				var twin = hash is null
					? default
					: sameStyle.FirstOrDefault(other => other.Fingerprint.Value == hash);
				if (twin.FaceId is not null)
				{
					// A stored fontFaceId may still name the duplicate, so it stays servable and maps to its twin.
					aliases[faceId] = twin.FaceId;
					continue;
				}

				sameStyle.Add((faceId, fingerprint));
			}
			else
			{
				listed[key] = [(faceId, fingerprint)];
			}

			faces.Add(new FontFaceInfo(faceId,
				entry.Family,
				entry.Weight,
				entry.Width,
				SlantId(entry.Slant),
				StyleName(style),
				entry.RemoteRenderable));
		}

		AddFontFiles(additionalFontDirectories, faces, sources);

		return new Catalog(faces, sources, aliases);
	}

	internal static IEnumerable<SystemFaceEntry> EnumerateSystemFaces()
	{
		var manager = SKFontManager.Default;
		var families = manager.FontFamilies
			.Where(family => !string.IsNullOrWhiteSpace(family))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(family => family, StringComparer.OrdinalIgnoreCase);

		foreach (var family in families)
		{
			using var styles = manager.GetFontStyles(family);
			for (var index = 0; index < styles.Count; index++)
			{
				var styleIndex = index;
				using var style = styles[styleIndex];
				using var typeface = styles.CreateTypeface(styleIndex);

				yield return new SystemFaceEntry(family,
					styleIndex,
					style.Weight,
					style.Width,
					style.Slant,
					typeface is not null && SfntFaceExtractor.CanExtract(typeface),
					() => FingerprintOf(styles, styleIndex));
			}
		}
	}

	private static string? FingerprintOf(SKFontStyleSet styles, int styleIndex)
	{
		if (styleIndex >= styles.Count)
		{
			return null;
		}

		using var typeface = styles.CreateTypeface(styleIndex);
		return typeface is null ? null : SfntFaceExtractor.Fingerprint(typeface);
	}

	// System faces are minted first and untouched: their ids are persisted in widget configuration.
	private static void AddFontFiles(
		IReadOnlyList<string> directories,
		List<FontFaceInfo> faces,
		Dictionary<string, FaceSource> sources)
	{
		var known = faces
			.Select(face => new FaceKey(face.Family.ToUpperInvariant(), face.Weight, face.Width, face.Slant))
			.ToHashSet();
		var found = new List<(FaceSource Source, bool RemoteRenderable)>();

		foreach (var path in directories.SelectMany(EnumerateFontFiles))
		{
			var faceCount = CountFaces(path);
			for (var index = 0; index < faceCount; index++)
			{
				using var typeface = OpenFace(path, index);
				if (typeface is null || string.IsNullOrWhiteSpace(typeface.FamilyName))
				{
					continue;
				}

				var source = new FaceSource(typeface.FamilyName,
					index,
					typeface.FontWeight,
					typeface.FontWidth,
					typeface.FontSlant,
					path);
				if (!known.Add(new FaceKey(source.Family.ToUpperInvariant(), source.Weight, source.Width, SlantId(source.Slant))))
				{
					continue;
				}

				found.Add((source, SfntFaceExtractor.CanExtract(typeface)));
			}
		}

		foreach (var (source, remoteRenderable) in found.OrderBy(entry => entry.Source.Family, StringComparer.OrdinalIgnoreCase))
		{
			using var style = new SKFontStyle(source.Weight, source.Width, source.Slant);
			var faceId = ReserveFaceId(sources, source.Family, style);
			sources[faceId] = source;
			faces.Add(new FontFaceInfo(faceId,
				source.Family,
				source.Weight,
				source.Width,
				SlantId(source.Slant),
				StyleName(style),
				remoteRenderable));
		}
	}

	private static IEnumerable<string> EnumerateFontFiles(string directory)
	{
		var options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			MaxRecursionDepth = MaxDirectoryDepth
		};

		try
		{
			return Directory.EnumerateFiles(directory, "*", options)
				.Where(path => FontFileExtensions.Contains(Path.GetExtension(path)))
				.Order(StringComparer.Ordinal)
				.ToList();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

	private static int CountFaces(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			Span<byte> header = stackalloc byte[12];
			if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
			{
				return 0;
			}

			return BinaryPrimitives.ReadUInt32BigEndian(header) == CollectionSignature
				? (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header[8..]), 256)
				: 1;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return 0;
		}
	}

	private static SKTypeface? OpenFace(string path, int index) => SKTypeface.FromFile(path, index);

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

	private sealed record FaceSource(
		string Family,
		int StyleIndex,
		int Weight,
		int Width,
		SKFontStyleSlant Slant,
		string? FilePath = null);

	private readonly record struct FaceKey(string Family, int Weight, int Width, string Slant);

	internal sealed record SystemFaceEntry(
		string Family,
		int StyleIndex,
		int Weight,
		int Width,
		SKFontStyleSlant Slant,
		bool RemoteRenderable,
		Func<string?> Fingerprint);

	private sealed record Catalog(
		IReadOnlyList<FontFaceInfo> Faces,
		IReadOnlyDictionary<string, FaceSource> Sources,
		IReadOnlyDictionary<string, string> Aliases);
}
