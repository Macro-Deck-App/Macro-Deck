using MacroDeck.Localization.Compiler;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Derives a manifest's <c>languages</c> from a plugin project's own resource files, so the list a store
/// reads before installing agrees with the catalog the plugin serves once it is running.
/// <para>
/// Reads the resource set exactly the way the localization generator does - the same
/// <c>Localization/**/*.resx</c> glob <c>build/MacroDeck.Plugin.Analyzers.props</c> feeds it, the same
/// file-name split, the same culture-name rule, and the same "an unsuffixed <c>Strings.resx</c> is
/// <see cref="LocalizationCompilation.DefaultCulture" />" convention - rather than a second, parallel
/// notion of what a plugin's languages are.
/// </para>
/// <para>
/// This needs the <em>project</em> tree, not a build output: a culture-suffixed <c>.resx</c> never becomes
/// a satellite assembly here (see <c>MacroDeck.Localization.csproj</c>), it is compiled into the generated
/// catalog, so nothing in a published directory can be read back. Discovery therefore yields nothing when
/// it is pointed at a staged or published payload, and callers carry the manifest's own value in that case.
/// </para>
/// </summary>
internal static class PluginLanguages
{
	/// <summary>The folder name the generator globs, and the only one searched here.</summary>
	public const string ResourceDirectoryName = "Localization";

	/// <summary>
	/// Every language <paramref name="projectDirectory" />'s resource files ship, as BCP-47 tags, ordered
	/// so two packs of the same tree produce byte-identical manifests. Empty when the directory holds no
	/// resource set - never an exception, and never a partial list from a directory that could not be read.
	/// </summary>
	public static IReadOnlyList<string> Discover(string projectDirectory)
	{
		var resourceRoot = Path.Combine(projectDirectory, ResourceDirectoryName);

		List<string> files;
		try
		{
			if (!Directory.Exists(resourceRoot))
			{
				return [];
			}

			files = [.. Directory.EnumerateFiles(resourceRoot, "*.resx", SearchOption.AllDirectories)];
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return [];
		}

		// Case-insensitive: a tag is case-insensitive by BCP 47, and the manifest reader rejects 'de'
		// alongside 'DE' as a duplicate - so a project whose files disagree on case must still pack.
		var languages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in files)
		{
			if (!ResxDocument.TrySplitFileName(Path.GetFileName(file), out _, out var culture))
			{
				continue;
			}

			if (culture is null)
			{
				languages.Add(LocalizationCompilation.DefaultCulture);
				continue;
			}

			// A malformed suffix is MDLOC005 at compile time; it is not this command's job to report it a
			// second time, and a tag the SDK would refuse to compile must not reach the manifest.
			if (LocalizationCultureName.IsValid(culture))
			{
				languages.Add(culture);
			}
		}

		return [.. languages];
	}

	/// <summary>
	/// Reports that <paramref name="discovered" /> replaced a different <paramref name="authored" /> list -
	/// the one outcome worth a word, since the manifest said one thing and the resource files another.
	/// Empty when nothing was discovered (the declared list is being carried, not replaced), when nothing
	/// was declared, or when the two name the same set.
	/// </summary>
	public static IReadOnlyList<CliDiagnostic> RecomputedWarnings(IReadOnlyList<string>? authored,
		IReadOnlyList<string> discovered)
	{
		if (authored is not { Count: > 0 } || discovered.Count == 0 || SameSet(authored, discovered))
		{
			return [];
		}

		return
		[
			new CliDiagnostic("languages-recomputed",
				$"'languages' was recomputed from '{ResourceDirectoryName}/' as " +
				$"[{string.Join(", ", discovered)}]; the manifest declared [{string.Join(", ", authored)}].")
		];
	}

	private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
		new HashSet<string>(left, StringComparer.OrdinalIgnoreCase).SetEquals(right);
}
