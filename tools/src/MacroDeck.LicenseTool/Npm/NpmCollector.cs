using System.Text.Json;
using MacroDeck.LicenseTool.Configuration;
using MacroDeck.LicenseTool.Model;

namespace MacroDeck.LicenseTool.Npm;

internal sealed class NpmCollector
{
	private static readonly string[] ScriptExtensions = [".ts", ".tsx", ".mts", ".cts", ".js", ".mjs", ".cjs"];

	private static readonly string[] StyleExtensions = [".scss", ".sass", ".css"];

	private static readonly string[] ExcludedDirectories = ["node_modules", "dist", "test", "tests", "__tests__", "e2e"];

	private static readonly JsonDocumentOptions JsoncOptions = new()
	{
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	private readonly string _repositoryRoot;
	private readonly string _npmRoot;
	private readonly NpmSourceConfiguration _source;
	private readonly Problems _problems;
	private readonly Dictionary<string, JsonElement> _lock;
	private readonly HashSet<string> _scannedFiles = new(StringComparer.Ordinal);
	private readonly SortedSet<string> _shippedKeys = new(StringComparer.Ordinal);

	private NpmCollector(string repositoryRoot, NpmSourceConfiguration source, Problems problems, JsonDocument lockDocument)
	{
		_repositoryRoot = repositoryRoot;
		_npmRoot = Path.Combine(repositoryRoot, source.Root);
		_source = source;
		_problems = problems;
		_lock = lockDocument.RootElement.GetProperty("packages").EnumerateObject()
			.ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
	}

	public static bool Collect(string repositoryRoot, NpmSourceConfiguration source, ComponentSet components, Problems problems)
	{
		var lockPath = Path.Combine(repositoryRoot, source.Root, "package-lock.json");
		if (!File.Exists(lockPath))
		{
			problems.Add($"npm: {Relative(repositoryRoot, lockPath)} is missing");
			return false;
		}

		using var lockDocument = JsonDocument.Parse(File.ReadAllText(lockPath));
		if (!lockDocument.RootElement.TryGetProperty("packages", out _))
		{
			problems.Add($"npm: {Relative(repositoryRoot, lockPath)} has no packages map; lockfileVersion 2 or 3 is required");
			return false;
		}

		var collector = new NpmCollector(repositoryRoot, source, problems, lockDocument);
		collector.Walk();
		return collector.AddComponents(components);
	}

	private void Walk()
	{
		foreach (var workspace in _source.Workspaces)
		{
			var workspaceDirectory = Path.Combine(_npmRoot, workspace.Path);
			if (_lock.TryGetValue(workspace.Path, out var entry))
			{
				foreach (var name in DependencyNames(entry, "dependencies").Concat(DependencyNames(entry, "optionalDependencies")))
				{
					ResolvePackage(workspace.Path, name, required: true, $"{workspace.Path}/package.json");
				}
			}
			else
			{
				_problems.Add($"npm: workspace {workspace.Path} is not in package-lock.json");
			}

			var aliases = ReadAliases(workspace, workspaceDirectory);
			foreach (var pattern in workspace.Entries)
			{
				var matches = Glob.Expand(workspaceDirectory, pattern);
				if (matches.Count == 0)
				{
					_problems.Add($"npm: entry {workspace.Path}/{pattern} matches no file");
				}

				foreach (var match in matches)
				{
					ScanFile(workspace, aliases, Path.GetFullPath(Path.Combine(workspaceDirectory, match)));
				}
			}

			foreach (var styleRoot in workspace.StyleRoots)
			{
				var directory = Path.Combine(workspaceDirectory, styleRoot);
				if (!Directory.Exists(directory))
				{
					_problems.Add($"npm: style root {workspace.Path}/{styleRoot} does not exist");
					continue;
				}

				foreach (var file in EnumerateStylesheets(directory))
				{
					ScanFile(workspace, aliases, file);
				}
			}
		}
	}

	private static IEnumerable<string> EnumerateStylesheets(string directory) =>
		Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
			.Where(file => StyleExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
			.Where(file => !Path.GetRelativePath(directory, file).Replace('\\', '/').Split('/')
				.Any(segment => ExcludedDirectories.Contains(segment, StringComparer.Ordinal)))
			.Order(StringComparer.Ordinal);

	private void ScanFile(NpmWorkspaceConfiguration workspace, IReadOnlyList<(string Key, List<string> Targets)> aliases, string file)
	{
		if (!_scannedFiles.Add(file))
		{
			return;
		}

		var extension = Path.GetExtension(file);
		var source = File.ReadAllText(file);
		if (StyleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
		{
			var lineComments = !string.Equals(extension, ".css", StringComparison.OrdinalIgnoreCase);
			foreach (var target in StylesheetImportScanner.Scan(source, lineComments))
			{
				ResolveStylesheetTarget(workspace, aliases, file, target);
			}

			return;
		}

		if (!ScriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
		{
			return;
		}

		foreach (var specifier in ScriptImportScanner.Scan(source))
		{
			ResolveScriptSpecifier(workspace, aliases, file, specifier);
		}
	}

	private void ResolveScriptSpecifier(
		NpmWorkspaceConfiguration workspace,
		IReadOnlyList<(string Key, List<string> Targets)> aliases,
		string file,
		string specifier)
	{
		if (specifier.StartsWith("node:", StringComparison.Ordinal) || specifier.Length == 0)
		{
			return;
		}

		if (specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal)
			|| specifier is "." or "..")
		{
			var resolved = ResolveScriptFile(Path.Combine(Path.GetDirectoryName(file)!, specifier));
			if (resolved is null)
			{
				_problems.Add($"npm: {Relative(_repositoryRoot, file)}: import '{specifier}' does not resolve to a file");
				return;
			}

			ScanFile(workspace, aliases, resolved);
			return;
		}

		foreach (var (key, targets) in aliases)
		{
			var matched = MatchAlias(key, specifier);
			if (matched is null)
			{
				continue;
			}

			var resolved = targets.Select(target => ResolveScriptFile(target.Replace("*", matched, StringComparison.Ordinal)))
				.FirstOrDefault(candidate => candidate is not null);
			if (resolved is null)
			{
				_problems.Add($"npm: {Relative(_repositoryRoot, file)}: alias import '{specifier}' does not resolve");
				return;
			}

			ScanFile(workspace, aliases, resolved);
			return;
		}

		ResolvePackage(workspace.Path, PackageName(specifier), required: true, Relative(_repositoryRoot, file));
	}

	private void ResolveStylesheetTarget(
		NpmWorkspaceConfiguration workspace,
		IReadOnlyList<(string Key, List<string> Targets)> aliases,
		string file,
		string target)
	{
		if (target.StartsWith("sass:", StringComparison.Ordinal) || target.StartsWith("http:", StringComparison.Ordinal)
			|| target.StartsWith("https:", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal))
		{
			return;
		}

		var location = Relative(_repositoryRoot, file);
		if (target.StartsWith('~'))
		{
			ResolvePackage(workspace.Path, PackageName(target[1..]), required: true, location);
			return;
		}

		if (target.StartsWith('/'))
		{
			_problems.Add($"npm: {location}: absolute stylesheet load '{target}' is not supported");
			return;
		}

		var local = ResolveStylesheetFile(Path.Combine(Path.GetDirectoryName(file)!, target));
		if (local is not null)
		{
			ScanFile(workspace, aliases, local);
			return;
		}

		if (target.StartsWith("./", StringComparison.Ordinal) || target.StartsWith("../", StringComparison.Ordinal))
		{
			_problems.Add($"npm: relative stylesheet load not found: {location}: {target}");
			return;
		}

		ResolvePackage(workspace.Path, PackageName(target), required: true, location);
	}

	private void ResolvePackage(string fromKey, string name, bool required, string requester)
	{
		if (name.StartsWith("@types/", StringComparison.Ordinal))
		{
			return;
		}

		var key = FindPackageKey(fromKey, name);
		if (key is null)
		{
			if (required)
			{
				_problems.Add($"npm: {requester}: package '{name}' is not in package-lock.json");
			}

			return;
		}

		var entry = _lock[key];
		if (entry.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.True)
		{
			var resolved = entry.TryGetProperty("resolved", out var target) ? target.GetString() : null;
			if (!_source.Workspaces.Any(workspace => workspace.Path == resolved))
			{
				_problems.Add($"npm: {requester}: workspace package '{name}' ({resolved}) is not configured in third-party/config.yml");
			}

			return;
		}

		if (entry.TryGetProperty("os", out _) || entry.TryGetProperty("cpu", out _) || !_shippedKeys.Add(key))
		{
			return;
		}

		foreach (var dependency in DependencyNames(entry, "dependencies"))
		{
			ResolvePackage(key, dependency, required: true, key);
		}

		foreach (var dependency in DependencyNames(entry, "optionalDependencies").Concat(DependencyNames(entry, "peerDependencies")))
		{
			ResolvePackage(key, dependency, required: false, key);
		}
	}

	private string? FindPackageKey(string fromKey, string name)
	{
		var directory = fromKey;
		while (true)
		{
			var candidate = directory.Length == 0 ? $"node_modules/{name}" : $"{directory}/node_modules/{name}";
			if (_lock.ContainsKey(candidate))
			{
				return candidate;
			}

			if (directory.Length == 0)
			{
				return null;
			}

			var nested = directory.LastIndexOf("node_modules/", StringComparison.Ordinal);
			directory = nested >= 0
				? directory[..nested].TrimEnd('/')
				: directory.Contains('/', StringComparison.Ordinal) ? directory[..directory.LastIndexOf('/')] : "";
		}
	}

	private bool AddComponents(ComponentSet components)
	{
		var complete = true;
		foreach (var key in _shippedKeys)
		{
			var entry = _lock[key];
			var name = key[(key.LastIndexOf("node_modules/", StringComparison.Ordinal) + "node_modules/".Length)..];
			var component = new Component(Ecosystem.Npm, name);
			if (entry.TryGetProperty("version", out var version))
			{
				component.Versions.Add(version.GetString()!);
			}

			if (LicenseOf(entry) is { } license)
			{
				component.DeclaredLicenses.Add(license);
			}

			var directory = Path.Combine(_npmRoot, key);
			if (!Directory.Exists(directory))
			{
				_problems.Add($"npm: {Relative(_repositoryRoot, directory)} is missing; run npm ci in {_source.Root}");
				components.Add(component);
				complete = false;
				continue;
			}

			ReadPackageJson(Path.Combine(directory, "package.json"), component);
			foreach (var file in Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal))
			{
				if (LicenseFiles.Classify(file!) is { } kind)
				{
					component.AddText(new LicenseText(file!, File.ReadAllText(Path.Combine(directory, file!)), kind));
				}
			}

			components.Add(component);
		}

		return complete;
	}

	private static void ReadPackageJson(string path, Component component)
	{
		if (!File.Exists(path))
		{
			return;
		}

		using var document = JsonDocument.Parse(File.ReadAllText(path), JsoncOptions);
		var root = document.RootElement;
		var repository = root.TryGetProperty("repository", out var repositoryElement)
			? repositoryElement.ValueKind == JsonValueKind.String
				? repositoryElement.GetString()
				: repositoryElement.TryGetProperty("url", out var url) ? url.GetString() : null
			: null;
		var homepage = root.TryGetProperty("homepage", out var homepageElement) && homepageElement.ValueKind == JsonValueKind.String
			? homepageElement.GetString()
			: null;
		component.Url = TextNormalizer.CleanUrl(repository ?? homepage);
		if (repository is not null && repository.StartsWith("github:", StringComparison.Ordinal))
		{
			component.Url = "https://github.com/" + repository["github:".Length..];
		}
		else if (repository is not null && !repository.Contains(':', StringComparison.Ordinal)
			&& repository.Count(character => character == '/') == 1)
		{
			component.Url = "https://github.com/" + repository;
		}

		if (root.TryGetProperty("author", out var author))
		{
			component.Copyright = author.ValueKind == JsonValueKind.String
				? author.GetString()
				: author.TryGetProperty("name", out var authorName) ? authorName.GetString() : null;
		}
	}

	private static string? LicenseOf(JsonElement entry)
	{
		if (!entry.TryGetProperty("license", out var license))
		{
			return null;
		}

		return license.ValueKind switch
		{
			JsonValueKind.String => license.GetString(),
			JsonValueKind.Object when license.TryGetProperty("type", out var type) => type.GetString(),
			_ => null,
		};
	}

	private List<(string Key, List<string> Targets)> ReadAliases(NpmWorkspaceConfiguration workspace, string workspaceDirectory)
	{
		if (workspace.Tsconfig is null)
		{
			return [];
		}

		var path = Path.Combine(workspaceDirectory, workspace.Tsconfig);
		if (!File.Exists(path))
		{
			_problems.Add($"npm: {Relative(_repositoryRoot, path)} does not exist");
			return [];
		}

		using var document = JsonDocument.Parse(File.ReadAllText(path), JsoncOptions);
		if (!document.RootElement.TryGetProperty("compilerOptions", out var options)
			|| !options.TryGetProperty("paths", out var paths))
		{
			return [];
		}

		var baseDirectory = options.TryGetProperty("baseUrl", out var baseUrl)
			? Path.Combine(Path.GetDirectoryName(path)!, baseUrl.GetString()!)
			: Path.GetDirectoryName(path)!;
		return paths.EnumerateObject()
			.Select(alias => (alias.Name, alias.Value.EnumerateArray()
				.Select(target => Path.GetFullPath(Path.Combine(baseDirectory, target.GetString()!)))
				.ToList()))
			.ToList();
	}

	private static string? MatchAlias(string key, string specifier)
	{
		var star = key.IndexOf('*', StringComparison.Ordinal);
		if (star < 0)
		{
			return key == specifier ? "" : null;
		}

		var prefix = key[..star];
		var suffix = key[(star + 1)..];
		return specifier.StartsWith(prefix, StringComparison.Ordinal) && specifier.EndsWith(suffix, StringComparison.Ordinal)
			&& specifier.Length >= prefix.Length + suffix.Length
				? specifier[prefix.Length..^suffix.Length]
				: null;
	}

	private static string? ResolveScriptFile(string path)
	{
		var full = Path.GetFullPath(path);
		var candidates = new List<string> { full };
		candidates.AddRange(ScriptExtensions.Select(extension => full + extension));
		candidates.Add(full + ".json");
		var extension = Path.GetExtension(full);
		if (extension is ".js" or ".mjs" or ".cjs")
		{
			var stem = full[..^extension.Length];
			candidates.AddRange([stem + ".ts", stem + ".tsx", stem + ".mts", stem + ".cts"]);
		}

		candidates.AddRange(ScriptExtensions.Select(candidate => Path.Combine(full, "index" + candidate)));
		return candidates.FirstOrDefault(File.Exists);
	}

	private static string? ResolveStylesheetFile(string path)
	{
		var full = Path.GetFullPath(path);
		var directory = Path.GetDirectoryName(full)!;
		var name = Path.GetFileName(full);
		string[] candidates =
		[
			full,
			Path.Combine(directory, $"_{name}.scss"),
			Path.Combine(directory, $"{name}.scss"),
			Path.Combine(directory, $"_{name}.sass"),
			Path.Combine(directory, $"{name}.sass"),
			Path.Combine(directory, $"_{name}.css"),
			Path.Combine(directory, $"{name}.css"),
			Path.Combine(full, "_index.scss"),
			Path.Combine(full, "index.scss"),
			Path.Combine(full, "_index.sass"),
			Path.Combine(full, "index.sass"),
		];
		return candidates.FirstOrDefault(File.Exists);
	}

	private static IEnumerable<string> DependencyNames(JsonElement entry, string property) =>
		entry.TryGetProperty(property, out var dependencies)
			? dependencies.EnumerateObject().Select(dependency => dependency.Name)
			: [];

	private static string PackageName(string specifier)
	{
		var segments = specifier.Split('/');
		return specifier.StartsWith('@') && segments.Length > 1 ? $"{segments[0]}/{segments[1]}" : segments[0];
	}

	private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
