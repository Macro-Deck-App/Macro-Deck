using System.Text.Json;
using System.Xml.Linq;
using MacroDeckHost.Rendering;

namespace MacroDeckHost.Tests.Rendering;

internal static class Fixtures
{
	public static readonly DateTimeOffset FixedNow = new(2026, 9, 12, 14, 5, 30, TimeSpan.Zero);

	private static readonly Lazy<string> _root = new(FindRoot);

	private static readonly Lazy<IRenderFonts> _fonts = new(() =>
		new RenderFonts(new RenderFontFace("Anta",
			File.ReadAllBytes(Path.Combine(RepoRoot, Manifest.GetProperty("font").GetString()!)))));

	private static readonly Lazy<EnglishCatalog> _catalog = new(() => new EnglishCatalog(
		Path.Combine(RepoRoot, "host", "src", "MacroDeckHost.Localization", "Localization", "Strings.resx")));

	public static string RepoRoot => _root.Value;

	public static string ProfileDirectory => Path.Combine(RepoRoot, "ui-model", "fixtures", "component-profile");

	public static JsonElement Manifest => Read(Path.Combine(ProfileDirectory, "measured-scenes.json"));

	public static IRenderFonts Fonts => _fonts.Value;

	public static IRenderLocalization Catalog => _catalog.Value;

	public static JsonElement Read(string path)
	{
		using var document = JsonDocument.Parse(File.ReadAllText(path));
		return document.RootElement.Clone();
	}

	public static JsonElement Profile(string file)
	{
		return Read(Path.Combine(ProfileDirectory, file));
	}

	public static TileRenderInput Input(JsonElement tree, double width, double height, double basis)
	{
		return new TileRenderInput
		{
			Tree = tree,
			Width = width,
			Height = height,
			Basis = basis,
			Theme = RenderTheme.Dark,
			Culture = Manifest.GetProperty("locale").GetString()!,
			TimeZone = TimeZoneInfo.Utc,
			Now = FixedNow,
			Localization = Catalog,
			Fonts = Fonts,
			Scale = 1,
			AnimatedImages = false,
			OwnsRootBorder = false
		};
	}

	private static string FindRoot()
	{
		for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			directory is not null;
			directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException("the repository root was not found above the test directory");
	}

	private sealed class EnglishCatalog : IRenderLocalization
	{
		private readonly Dictionary<string, string> _strings;

		public EnglishCatalog(string path)
		{
			_strings = XDocument.Load(path).Root!.Elements("data")
				.Where(data => data.Attribute("name") is not null && data.Element("value") is not null)
				.ToDictionary(data => data.Attribute("name")!.Value,
					data => data.Element("value")!.Value,
					StringComparer.Ordinal);
		}

		public string Translate(string scope, string key, IReadOnlyDictionary<string, string>? arguments)
		{
			if (scope != "macrodeck.app" || !_strings.TryGetValue(key, out var template))
			{
				return $"[[{scope}:{key}]]";
			}

			foreach (var (name, value) in arguments ?? new Dictionary<string, string>())
			{
				template = template.Replace($"{{{name}}}", value, StringComparison.Ordinal);
			}

			return template;
		}
	}
}
