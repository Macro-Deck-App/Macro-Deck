using System.Collections.Concurrent;
using MacroDeckHost.Rendering.Text;
using SkiaSharp;

namespace MacroDeckHost.Rendering;

public sealed class RenderFontFace : IDisposable
{
	public RenderFontFace(string name, byte[] data)
	{
		Name = name;
		Opened = FontFace.FromData(name, data)
			?? throw new ArgumentException($"Font '{name}' cannot be read for both shaping and drawing.",
				nameof(data));
	}

	private RenderFontFace(FontFace face)
	{
		Name = face.Name;
		Opened = face;
	}

	public string Name { get; }

	internal FontFace Opened { get; }

	public void Dispose()
	{
		Opened.Dispose();
	}

	internal static RenderFontFace? FromTypeface(SKTypeface typeface)
	{
		return FontFace.FromTypeface(typeface) is { } face ? new RenderFontFace(face) : null;
	}
}

public interface IRenderFonts
{
	IReadOnlyList<RenderFontFace> Faces(string family, string culture);

	RenderFontFace? Fallback(int codepoint, string culture);
}

public sealed class RenderFonts : IRenderFonts
{
	private static readonly string[] _sansStack = ["Segoe UI", "Roboto", "Helvetica Neue", "Arial", "DejaVu Sans"];

	private static readonly string[] _monoStack =
	[
		"SF Mono", "SFMono-Regular", "Menlo", "Consolas", "DejaVu Sans Mono",
		"Liberation Mono", "Courier New"
	];

	private static readonly Dictionary<string, string[]> _languageStacks = new(StringComparer.OrdinalIgnoreCase)
	{
		["ja"] =
		[
			"Hiragino Sans", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", "Meiryo", "Noto Sans CJK JP", "Noto Sans JP"
		],
		["ko"] = ["Apple SD Gothic Neo", "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR"],
		["zh"] = ["PingFang SC", "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Noto Sans SC"],
		["zh-Hant"] =
		[
			"PingFang TC", "Microsoft JhengHei UI", "Microsoft JhengHei", "Noto Sans CJK TC", "Noto Sans TC"
		],
		["ar"] = ["Geeza Pro", "Noto Sans Arabic", "Tahoma"],
		["hi"] = ["Kohinoor Devanagari", "Nirmala UI", "Noto Sans Devanagari", "Mangal"]
	};

	// Installed faces live as long as the process: a font file is read once however many renders and
	// fallback lookups reach it, and a face is never disposed under a render still drawing with it.
	private static readonly ConcurrentDictionary<(string, int, int, SKFontStyleSlant), Lazy<RenderFontFace?>>
		_installed = new();

	private readonly RenderFontFace? _uiFace;

	private readonly bool _systemFaces;

	private readonly ConcurrentDictionary<string, IReadOnlyList<RenderFontFace>> _stacks = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<(int, string), RenderFontFace?> _fallbacks = new();

	public RenderFonts(RenderFontFace? uiFace, bool systemFaces = true)
	{
		_uiFace = uiFace;
		_systemFaces = systemFaces;
	}

	public IReadOnlyList<RenderFontFace> Faces(string family, string culture)
	{
		return _stacks.GetOrAdd($"{family}|{culture}", _ => BuildStack(family, culture));
	}

	public RenderFontFace? Fallback(int codepoint, string culture)
	{
		if (!_systemFaces)
		{
			return null;
		}

		return _fallbacks.GetOrAdd((codepoint, culture),
			key =>
			{
				var language = string.IsNullOrEmpty(key.Item2) ? [] : new[] { key.Item2 };
				return Installed(SKFontManager.Default.MatchCharacter(null, SKFontStyle.Normal, language, key.Item1));
			});
	}

	// The matched typeface is kept, never disposed: Skia hands the same managed wrapper back for a typeface
	// it already returned, so disposing one here could dispose a face another render is drawing with.
	private static RenderFontFace? Installed(SKTypeface? typeface)
	{
		if (typeface is null)
		{
			return null;
		}

		var key = (typeface.FamilyName, typeface.FontWeight, typeface.FontWidth, typeface.FontSlant);
		return _installed.GetOrAdd(key,
				_ => new Lazy<RenderFontFace?>(() => RenderFontFace.FromTypeface(typeface),
					LazyThreadSafetyMode.ExecutionAndPublication))
			.Value;
	}

	private List<RenderFontFace> BuildStack(string family, string culture)
	{
		var faces = new List<RenderFontFace>();
		if (family == "mono")
		{
			AddInstalled(faces, _monoStack);
		}
		else if (_uiFace is not null)
		{
			faces.Add(_uiFace);
		}

		if (_languageStacks.TryGetValue(LanguageKey(culture), out var stack))
		{
			AddInstalled(faces, stack);
		}

		if (faces.Count == 0)
		{
			AddInstalled(faces, _sansStack);
		}

		if (faces.Count == 0 && Installed(SKTypeface.Default) is { } fallback)
		{
			faces.Add(fallback);
		}

		return faces;
	}

	private void AddInstalled(List<RenderFontFace> faces, IEnumerable<string> names)
	{
		if (!_systemFaces)
		{
			return;
		}

		foreach (var name in names)
		{
			var typeface = SKFontManager.Default.MatchFamily(name);
			if (typeface is not null
				&& string.Equals(typeface.FamilyName, name, StringComparison.OrdinalIgnoreCase)
				&& Installed(typeface) is { } face)
			{
				faces.Add(face);
			}
		}
	}

	private static string LanguageKey(string culture)
	{
		if (culture is "zh-TW" or "zh-HK" or "zh-MO"
			|| culture.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
		{
			return "zh-Hant";
		}

		var dash = culture.IndexOf('-', StringComparison.Ordinal);
		return dash < 0 ? culture : culture[..dash];
	}
}
