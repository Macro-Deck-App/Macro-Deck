using MacroDeck.Localization.Compiler;

namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// The two acceptance criteria of issue #680 that nothing else would notice breaking: every key the app
/// can ask for is actually served, and every one of them is translated into every language Macro Deck
/// ships.
/// </summary>
/// <remarks>
/// Bundling the English text into the clients means a key the catalog does not carry now renders
/// plausible English rather than a conspicuous <c>[[scope:Key]]</c>, so "look at the running app" stopped
/// being a way to find a missing key. This is what replaces it.
/// </remarks>
[TestFixture]
public class ShippedCatalogTests
{
	private static readonly (string Scope, string[] Directory)[] _catalogs =
	[
		(LocalizationScope.MacroDeck, ["sdk", "src", "MacroDeck.Localization", "Resources"]),
		("macrodeck.app", ["host", "src", "MacroDeckHost.Localization", "Localization"]),
	];

	public static IEnumerable<TestCaseData> Catalogs()
	{
		foreach (var catalog in _catalogs)
		{
			yield return new TestCaseData(catalog.Scope, catalog.Directory).SetName(catalog.Scope);
		}
	}

	[TestCaseSource(nameof(Catalogs))]
	public void Every_shipped_catalog_compiles_without_a_finding(string scope, string[] directory)
	{
		var result = Compile(scope, directory);

		Assert.That(result.Findings.Select(finding => $"{finding.Id} {finding.Message}"), Is.Empty);
	}

	/// <summary>
	/// Every generated member resolves to a template. A plural family's member carries the base key, which
	/// is deliberately not a template of its own, so it is checked through its forms instead.
	/// </summary>
	[TestCaseSource(nameof(Catalogs))]
	public void Every_generated_key_is_served_by_the_catalog(string scope, string[] directory)
	{
		var result = Compile(scope, directory);
		var templates = result.Catalog[result.DefaultCulture];

		Assert.Multiple(() =>
		{
			foreach (var entry in result.Entries)
			{
				Assert.That(templates.ContainsKey(entry.Key) || IsPluralFamily(templates, entry.Key),
					Is.True,
					$"'{scope}:{entry.Key}' has a generated member but no text to resolve to");
			}
		});
	}

	/// <summary>
	/// The "translations are complete" criterion, enforced rather than asserted in a changelog. A key with
	/// no German value still renders - the fallback chain reaches English - which is exactly why nothing
	/// else would catch it.
	/// </summary>
	[TestCaseSource(nameof(Catalogs))]
	public void Every_key_is_translated_into_every_language_the_catalog_ships(string scope, string[] directory)
	{
		var result = Compile(scope, directory);
		var byDefault = result.Catalog[result.DefaultCulture];

		Assert.Multiple(() =>
		{
			foreach (var culture in result.Cultures)
			{
				if (string.Equals(culture, result.DefaultCulture, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var translated = result.Catalog[culture];

				foreach (var key in byDefault.Keys)
				{
					Assert.That(translated.ContainsKey(key),
						Is.True,
						$"'{scope}:{key}' has no {culture} translation");
					Assert.That(translated.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text),
						Is.True,
						$"'{scope}:{key}' has an empty {culture} translation");
				}
			}
		});
	}

	private static bool IsPluralFamily(Dictionary<string, string> templates, string baseKey)
		=> templates.ContainsKey(baseKey + ".Other");

	private static LocalizationCompilationResult Compile(string scope, string[] directory)
	{
		var set = new LocalizationResourceSet(scope, "Strings");
		var path = RepositoryDirectory(directory);

		foreach (var file in Directory.EnumerateFiles(path, "*.resx")
			.OrderBy(file => file, StringComparer.Ordinal))
		{
			var fileName = Path.GetFileName(file);
			Assert.That(ResxDocument.TrySplitFileName(fileName, out _, out var culture), Is.True);

			set.Files.Add(new LocalizationResourceFile(file,
				fileName,
				culture,
				ResxDocument.Parse(File.ReadAllText(file))));
		}

		return LocalizationCompilation.Compile(set);
	}

	private static string RepositoryDirectory(string[] segments)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return Path.Combine([directory.FullName, .. segments]);
			}

			directory = directory.Parent;
		}

		Assert.Fail("The repository root was not found from the test output directory.");

		return string.Empty;
	}
}
