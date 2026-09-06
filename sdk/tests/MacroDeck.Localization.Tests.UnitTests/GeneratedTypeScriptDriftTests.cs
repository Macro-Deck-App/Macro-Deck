using MacroDeck.Localization.Compiler;

namespace MacroDeck.Localization.Tests.UnitTests;

/// <summary>
/// The "a single localization compiler generates both the C# and the TypeScript APIs, and Angular keys
/// are generated rather than manually duplicated" acceptance criterion.
///
/// <para>
/// The Angular workspace has no .NET SDK in CI, so the TypeScript module is checked in and this test is
/// what keeps it honest - the same guard the checked-in protocol specifications already get. The
/// expectation is derived from the <c>.resx</c> resources, never from either generated artifact, so
/// editing one side alone fails here rather than drifting quietly.
/// </para>
/// </summary>
[TestFixture]
public class GeneratedTypeScriptDriftTests
{
	/// <summary>
	/// The catalogs the Angular clients import, each emitted as its own module. <c>macrodeck</c> is the
	/// reusable catalog the SDK publishes; <c>macrodeck.app</c> is the application's own, which ships with
	/// the host rather than in a package.
	/// </summary>
	private static readonly Catalog[] _catalogs =
	[
		new("Strings",
			"strings.ts",
			LocalizationScope.MacroDeck,
			["sdk", "src", "MacroDeck.Localization", "Resources"]),
		new("AppStrings",
			"app-strings.ts",
			"macrodeck.app",
			["host", "src", "MacroDeckHost.Localization", "Localization"]),
	];

	/// <summary>The keys the bootstrapper renders itself; everything else stays the host's to resolve.</summary>
	private const string _bootstrapperKeyPrefix = "Bootstrapper.";

	/// <summary>
	/// The key namespaces a deck client paints itself. Everything else the host serves, so a client that
	/// carried it would be carrying the configuration UI's catalog in order to render a deck.
	/// </summary>
	private static readonly string[] _webClientKeyPrefixes =
	[
		"Auth.",
		"Deck.FolderView.",
		"Errors.Auth.",
		"Errors.Folder.",
		"Feedback.",
		"KeyRing.Unlock.",
		"Settings.Appearance.",
		"Settings.Network.Tls.",
		"WebClient.",
	];

	[TestCaseSource(nameof(_catalogs))]
	public void The_checked_in_TypeScript_module_matches_a_fresh_generation(Catalog catalog)
	{
		var expected = Generate(catalog);
		var path = RepositoryFile("ui",
			"runtime",
			"src",
			"localization",
			"generated",
			catalog.FileName);

		// The generator has no other driver: the Angular CI job has no .NET SDK, so the module is checked
		// in and regenerated deliberately rather than on every build.
		if (Environment.GetEnvironmentVariable("MACRODECK_UPDATE_GENERATED") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, expected);
			Assert.Pass($"Regenerated {path}.");
		}

		Assert.That(File.Exists(path), Is.True, $"{path} is missing; regenerate it from the resources.");

		var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

		Assert.That(actual,
			Is.EqualTo(expected),
			"the checked-in TypeScript keys no longer match the .resx resources they are generated from. " +
			"Regenerate with MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests");
	}

	/// <summary>
	/// The bootstrapper compiles its own slice in, for the text it must render before the host can answer.
	/// Same guard, same reason: the Rust build has no .NET SDK, so the module is checked in.
	/// </summary>
	[Test]
	public void The_checked_in_Rust_module_matches_a_fresh_generation()
	{
		var catalog = _catalogs[1];
		var expected = LocalizationRustEmitter.Emit(_bootstrapperKeyPrefix, CompileResources(catalog));
		var path = RepositoryFile("ui", "bootstrapper", "src", "localization", "generated.rs");

		if (Environment.GetEnvironmentVariable("MACRODECK_UPDATE_GENERATED") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, expected);
			Assert.Pass($"Regenerated {path}.");
		}

		Assert.That(File.Exists(path), Is.True, $"{path} is missing; regenerate it from the resources.");

		var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

		Assert.That(actual,
			Is.EqualTo(expected),
			"the checked-in Rust catalog no longer matches the .resx resources it is generated from. " +
			"Regenerate with MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests");
	}

	/// <summary>
	/// The web client compiles in only the slice it can render. The whole application catalog is half a
	/// megabyte of strings a deck never paints, on hardware with an iOS 9 / Chrome 30 floor (#833, #824).
	/// Same guard as the modules above, for the same reason: generation has no other driver.
	/// </summary>
	[Test]
	public void The_checked_in_web_client_TypeScript_module_matches_a_fresh_generation()
	{
		var expected = GenerateWebClientSlice();
		var path = RepositoryFile("ui",
			"runtime",
			"src",
			"localization",
			"generated",
			"client-app-strings.ts");

		if (Environment.GetEnvironmentVariable("MACRODECK_UPDATE_GENERATED") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, expected);
			Assert.Pass($"Regenerated {path}.");
		}

		Assert.That(File.Exists(path), Is.True, $"{path} is missing; regenerate it from the resources.");

		var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

		Assert.That(actual,
			Is.EqualTo(expected),
			"the checked-in web client catalog no longer matches the .resx resources it is generated from. " +
			"Regenerate with MACRODECK_UPDATE_GENERATED=1 dotnet test sdk/tests/MacroDeck.Localization.Tests.UnitTests");
	}

	/// <summary>
	/// The slice is a slice: every key it carries says what the whole catalog says it says, and it is
	/// narrow enough to be worth cutting. A narrowed module that reworded a key would leave one client
	/// painting text no other surface shows.
	/// </summary>
	[Test]
	public void The_web_client_slice_carries_the_wording_of_the_catalog_it_is_cut_from()
	{
		var whole = LocalizationTypeScriptEmitter.Emit("AppStrings", CompileResources(_catalogs[1]));
		var slice = GenerateWebClientSlice();
		var entries = DefaultEntries(slice);

		Assert.Multiple(() =>
		{
			foreach (var entry in entries)
			{
				Assert.That(whole, Does.Contain(entry), $"the slice reworded {entry}");
			}

			Assert.That(entries, Is.Not.Empty, "the configured prefixes match no key at all");
			Assert.That(entries,
				Has.Count.LessThan(DefaultEntries(whole).Count / 2),
				"a slice this wide is not worth cutting: the client may as well carry the catalog");
		});
	}

	private static string GenerateWebClientSlice()
		=> LocalizationTypeScriptEmitter.Emit("ClientAppStrings",
			CompileResources(_catalogs[1]),
			_webClientKeyPrefixes);

	/// <summary>The default-language lines of an emitted module, each <c>'scope:key': 'text',</c>.</summary>
	private static List<string> DefaultEntries(string module)
		=> module.Split('\n')
			.Select(line => line.Trim())
			.Where(line => line.StartsWith("'macrodeck.app:", StringComparison.Ordinal) &&
				line.EndsWith("',", StringComparison.Ordinal) &&
				line.Contains("': '", StringComparison.Ordinal))
			.ToList();

	[TestCaseSource(nameof(_catalogs))]
	public void Every_catalog_key_appears_in_both_generated_APIs(Catalog catalog)
	{
		var result = CompileResources(catalog);
		var typeScript = LocalizationTypeScriptEmitter.Emit(catalog.ConstName, result);

		Assert.Multiple(() =>
		{
			foreach (var entry in result.Entries)
			{
				Assert.That(typeScript,
					Does.Contain($"'{result.Scope}:{entry.Key}'"),
					$"'{entry.Key}' is in the C# API but not the TypeScript one");
			}
		});
	}

	private static string Generate(Catalog catalog)
		=> LocalizationTypeScriptEmitter.Emit(catalog.ConstName, CompileResources(catalog));

	private static LocalizationCompilationResult CompileResources(Catalog catalog)
	{
		var directory = RepositoryFile(catalog.Directory);
		var set = new LocalizationResourceSet(catalog.Scope, "Strings");

		foreach (var path in Directory.EnumerateFiles(directory, "*.resx").OrderBy(path => path,
			StringComparer.Ordinal))
		{
			var fileName = Path.GetFileName(path);

			Assert.That(ResxDocument.TrySplitFileName(fileName, out _, out var culture), Is.True);

			set.Files.Add(new LocalizationResourceFile(path,
				fileName,
				culture,
				ResxDocument.Parse(File.ReadAllText(path))));
		}

		var result = LocalizationCompilation.Compile(set);

		Assert.That(result.Findings, Is.Empty, "the shipped catalog must compile cleanly");

		return result;
	}

	/// <summary>One catalog's identity: what it is called in TypeScript and where its resources live.</summary>
	public sealed record Catalog(string ConstName, string FileName, string Scope, string[] Directory)
	{
		public override string ToString() => Scope;
	}

	/// <summary>Fails loudly rather than letting a missing repository pass the drift check vacuously.</summary>
	private static string RepositoryFile(params string[] segments)
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
