using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.LicenseTool.Collectors;

namespace MacroDeck.LicenseTool.Tests.UnitTests.Support;

internal sealed class FixtureRepository : IDisposable
{
	private const string Policy = """
		noticeHeader: |
		  Fixture product
		policy:
		  allowedLicenses: [MIT, ISC, BSD-3-Clause, Apache-2.0]
		  allowedExceptions: [LLVM-exception]
		""";

	private readonly JsonObject _assetsTargets = new();
	private readonly JsonObject _assetsLibraries = new();
	private readonly JsonObject _lockPackages = new();
	private readonly List<string> _sections = [];
	private string _overrides = "packages: []";
	private string _attributions = "assets: []";

	public FixtureRepository()
	{
		Root = Path.Combine(Path.GetTempPath(), "licensetool-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
		Write("third-party/licenses/MIT.txt", "MIT License\n\nStandard MIT text.");
		Write("third-party/licenses/Apache-2.0.txt", "Apache License\nVersion 2.0 standard text.");
		Write("third-party/licenses/ISC.txt", "ISC License\n\nStandard ISC text.");
		Write("third-party/licenses/BSD-3-Clause.txt", "BSD 3-Clause License\n\nStandard BSD text.");
	}

	public string Root { get; }

	public FakeCargoMetadata Cargo { get; } = new();

	public void Dispose() => Directory.Delete(Root, recursive: true);

	public void Write(string relativePath, string content)
	{
		var path = Path.Combine(Root, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}

	public void Overrides(string yaml) => _overrides = yaml;

	public void Attributions(string yaml) => _attributions = yaml;

	public void UseNuGet(params string[] runtimeIdentifiers) => UseNuGet(runtimeIdentifiers, runtimeIdentifiers);

	public void UseNuGet(string[] runtimeIdentifiers, string[] restoredRuntimeIdentifiers)
	{
		var rids = string.Join("\n", runtimeIdentifiers.Select(rid => $"      - id: {rid}\n        platform: {Platform(rid)}"));
		_sections.Add($"""
			nuget:
			  - project: host/App/App.csproj
			    targetFramework: net10.0
			    runtimeIdentifiers:
			{rids}
			""");
		foreach (var rid in restoredRuntimeIdentifiers)
		{
			_assetsTargets[$"net10.0/{rid}"] = new JsonObject();
		}
	}

	public void NuGetPackage(
		string id,
		string version,
		string? licenseExpression,
		IReadOnlyDictionary<string, string[]> nativeByRid,
		IReadOnlyDictionary<string, string>? rootFiles = null,
		string? licenseFile = null)
	{
		var folder = $"{id.ToLowerInvariant()}/{version}";
		var license = licenseExpression is not null
			? $"<license type=\"expression\">{licenseExpression}</license>"
			: licenseFile is not null ? $"<license type=\"file\">{licenseFile}</license>" : "";
		Write($"packages/{folder}/{id.ToLowerInvariant()}.nuspec", $"""
			<?xml version="1.0" encoding="utf-8"?>
			<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
			  <metadata>
			    <id>{id}</id>
			    <version>{version}</version>
			    <authors>{id} Authors</authors>
			    {license}
			    <projectUrl>https://example.com/{id}</projectUrl>
			  </metadata>
			</package>
			""");
		var files = new JsonArray($"{id.ToLowerInvariant()}.nuspec");
		foreach (var (name, content) in rootFiles ?? new Dictionary<string, string>())
		{
			Write($"packages/{folder}/{name}", content);
			files.Add(name);
		}

		_assetsLibraries[$"{id}/{version}"] = new JsonObject
		{
			["type"] = "package",
			["path"] = folder,
			["files"] = files,
		};
		foreach (var (targetName, target) in _assetsTargets)
		{
			var rid = targetName["net10.0/".Length..];
			var runtime = new JsonObject { ["lib/net10.0/_._"] = new JsonObject() };
			var native = new JsonObject();
			foreach (var asset in nativeByRid.GetValueOrDefault(rid) ?? nativeByRid.GetValueOrDefault("*") ?? [])
			{
				native[asset] = new JsonObject();
			}

			target!.AsObject()[$"{id}/{version}"] = new JsonObject
			{
				["type"] = "package",
				["runtime"] = runtime,
				["native"] = native,
			};
		}
	}

	public void UseNpm(string workspacesYaml)
	{
		_sections.Add($"""
			npm:
			  root: ui
			  workspaces:
			{workspacesYaml}
			""");
		_lockPackages[""] = new JsonObject { ["name"] = "root" };
	}

	public void NpmWorkspace(string path, params string[] dependencies) =>
		_lockPackages[path] = new JsonObject { ["dependencies"] = Dependencies(dependencies) };

	public void NpmPackage(
		string key,
		string version,
		string? license,
		string[]? dependencies = null,
		bool osRestricted = false,
		bool install = true,
		bool dev = false,
		string? licenseText = null)
	{
		var entry = new JsonObject { ["version"] = version };
		if (license is not null)
		{
			entry["license"] = license;
		}

		if (dependencies is not null)
		{
			entry["dependencies"] = Dependencies(dependencies);
		}

		if (osRestricted)
		{
			entry["os"] = new JsonArray("darwin");
		}

		if (dev)
		{
			entry["dev"] = true;
		}

		_lockPackages[key] = entry;
		if (install)
		{
			var name = key[(key.LastIndexOf("node_modules/", StringComparison.Ordinal) + "node_modules/".Length)..];
			Write($"ui/{key}/package.json", JsonSerializer.Serialize(new { name, version, repository = $"github:example/{name}" }));
			Write($"ui/{key}/LICENSE", licenseText ?? $"{license} license of {name} {version}");
		}
	}

	public void UseAssets(string root, string firstParty) =>
		_sections.Add($"""
			assets:
			  roots: [{root}]
			  extensions: [.svg, .png]
			  firstParty: [{firstParty}]
			""");

	public void UseCargo(params string[] triples)
	{
		var targets = string.Join("\n", triples.Select(triple => $"      - id: {triple}\n        platform: {Platform(triple)}"));
		_sections.Add($"""
			cargo:
			  - manifestDirectory: app
			    targets:
			{targets}
			""");
	}

	public GenerationResult Generate()
	{
		Write("third-party/config.yml", Policy + "\n" + string.Join("\n", _sections));
		Write("third-party/overrides.yml", _overrides);
		Write("third-party/attributions.yml", _attributions);
		if (_assetsTargets.Count > 0)
		{
			Write("host/App/obj/project.assets.json", new JsonObject
			{
				["targets"] = _assetsTargets.DeepClone(),
				["libraries"] = _assetsLibraries.DeepClone(),
				["packageFolders"] = new JsonObject { [Path.Combine(Root, "packages") + Path.DirectorySeparatorChar] = new JsonObject() },
			}.ToJsonString());
		}

		if (_lockPackages.Count > 0)
		{
			Write("ui/package-lock.json", new JsonObject
			{
				["lockfileVersion"] = 3,
				["packages"] = _lockPackages.DeepClone(),
			}.ToJsonString());
		}

		return NoticeGenerator.Generate(Root, Cargo);
	}

	public string CrateDirectory(string name, string version) => Path.Combine(Root, "cargo-registry", $"{name}-{version}");

	private static JsonObject Dependencies(IEnumerable<string> names)
	{
		var result = new JsonObject();
		foreach (var name in names)
		{
			result[name] = "*";
		}

		return result;
	}

	private static string Platform(string target) => target switch
	{
		_ when target.StartsWith("win", StringComparison.Ordinal) || target.Contains("windows", StringComparison.Ordinal) => "Windows",
		_ when target.StartsWith("osx", StringComparison.Ordinal) || target.Contains("darwin", StringComparison.Ordinal) => "macOS",
		_ => "Linux",
	};
}

internal sealed class FakeCargoMetadata : ICargoMetadataSource
{
	private readonly Dictionary<string, string> _byTriple = new(StringComparer.Ordinal);

	public void Set(string triple, string json) => _byTriple[triple] = json;

	public string Read(string manifestDirectory, string targetTriple) => _byTriple[targetTriple];
}
