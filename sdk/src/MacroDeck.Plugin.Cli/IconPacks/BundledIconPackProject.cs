using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.IconPacks;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.IconPacks;

internal sealed record BundledIconPackAddRequest
{
	public required string SourceDirectory { get; init; }

	public required string ManifestPath { get; init; }

	public required string PackPath { get; init; }

	public string? Key { get; init; }

	public bool Copy { get; init; }

	public bool Force { get; init; }
}

internal enum BundledIconPackPlacement
{
	Copied,
	Moved,
	InPlace
}

internal sealed record BundledIconPackOutcome
{
	public string? Key { get; init; }

	public string? TargetPath { get; init; }

	public string? PackName { get; init; }

	public int IconCount { get; init; }

	public BundledIconPackPlacement Placement { get; init; }

	public string? MovedFrom { get; init; }

	public bool Replaced { get; init; }

	public bool FileDeleted { get; init; }

	public IReadOnlyList<CliDiagnostic> Warnings { get; init; } = [];

	public CliDiagnostic? Error { get; init; }

	public string? ErrorDetail { get; init; }

	public int ExitCode { get; init; } = Cli.ExitCode.Success;

	public static BundledIconPackOutcome Fail(int exitCode,
		string code,
		string message,
		string? detail = null,
		IReadOnlyList<CliDiagnostic>? warnings = null)
		=> new()
		{
			ExitCode = exitCode,
			Error = new CliDiagnostic(code, message),
			ErrorDetail = detail,
			Warnings = warnings ?? []
		};
}

internal sealed record ProjectManifest
{
	public required string Root { get; init; }

	public required string ManifestPath { get; init; }

	public required JsonObject Document { get; init; }

	public required PluginManifest Manifest { get; init; }

	public required bool EndsWithNewline { get; init; }

	public required string NewLine { get; init; }

	public required JsonSerializerOptions WriteOptions { get; init; }
}

internal static class BundledIconPackProject
{
	private const string PropertyName = "bundledIconPacks";

	private static readonly JsonDocumentOptions _readOptions =
		new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

	private static readonly StringComparison _pathComparison =
		OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

	public static BundledIconPackOutcome Add(BundledIconPackAddRequest request)
	{
		if (Load(request.SourceDirectory, request.ManifestPath, out var project) is { } loadFailure)
		{
			return loadFailure;
		}

		var sourceFull = Path.GetFullPath(request.PackPath);
		if (!File.Exists(sourceFull))
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable,
				"icon-pack-not-found",
				$"No icon pack at '{CliText.DisplayPath(request.PackPath)}'.");
		}

		if (new FileInfo(sourceFull).Length > PluginArtifactLimits.MaxEntryBytes)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-too-large",
				$"'{CliText.DisplayPath(sourceFull)}' is larger than the {PluginArtifactLimits.MaxEntryBytes} bytes " +
				"a single plugin artifact entry may hold.");
		}

		var read = IconPackArchive.Read(sourceFull);
		if (read.Info is not { } info)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-invalid",
				$"'{CliText.DisplayPath(sourceFull)}' is not a usable icon pack: {read.Error}");
		}

		if (DescribeNameProblems(info) is { } nameProblems)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-names-invalid",
				$"'{CliText.DisplayPath(sourceFull)}' cannot be bundled, because its icons are not addressable by name.",
				nameProblems);
		}

		var warnings = new List<CliDiagnostic>();
		if (info.Ai is null)
		{
			warnings.Add(new CliDiagnostic("icon-pack-ai-undeclared",
				"The icon pack declares nothing about AI. Its icons ship as part of this plugin, so make sure the " +
				"plugin's own 'ai' declaration covers them."));
		}
		else if (info.Ai.GeneratedAssets && project.Manifest.Ai?.GeneratedAssets != true)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"ai-declaration-mismatch",
				"The icon pack declares AI-generated assets, but the plugin manifest does not. Set " +
				"'ai.generatedAssets' to true in manifest.json before bundling it.");
		}

		string key;
		if (request.Key is { } requestedKey)
		{
			if (!PluginBundledIconPacks.IsValidKey(requestedKey))
			{
				return BundledIconPackOutcome.Fail(ExitCode.UsageError,
					"invalid-key",
					$"'{requestedKey}' is not a valid key. Use lowercase letters, digits and inner hyphens, at most " +
					$"{PluginBundledIconPacks.MaxKeyLength} characters.");
			}

			key = requestedKey;
		}
		else if (PluginBundledIconPacks.SlugFromName(info.Name) is { } slug)
		{
			key = slug;
		}
		else
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"no-key",
				$"No key can be derived from the pack name '{info.Name}'. Pass --key <key>.");
		}

		var entries = ReadEntries(project.Document, out var entriesFailure);
		if (entriesFailure is not null)
		{
			return entriesFailure;
		}

		var existingIndex = entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
		var existing = existingIndex >= 0 ? entries[existingIndex] : null;

		if (existing is not null && !request.Force)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-exists",
				$"The manifest already bundles an icon pack under the key '{key}'. Pass --force to replace it, or " +
				"--key to choose another key.",
				warnings: warnings);
		}

		if (existing is null && entries.Count >= PluginBundledIconPacks.MaxCount)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"too-many-icon-packs",
				$"The manifest already bundles {PluginBundledIconPacks.MaxCount} icon packs, the most a plugin can " +
				"declare.",
				warnings: warnings);
		}

		var targetRelative = PluginBundledIconPacks.DefaultPath(key);
		var targetFull = Resolve(project.Root, targetRelative);

		if (entries.FirstOrDefault(entry => !string.Equals(entry.Key, key, StringComparison.Ordinal) &&
				string.Equals(entry.Path, targetRelative, StringComparison.OrdinalIgnoreCase)) is { } clash)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-path-in-use",
				$"'{targetRelative}' is already declared by the icon pack '{clash.Key}'.",
				warnings: warnings);
		}

		var inPlace = string.Equals(sourceFull, targetFull, _pathComparison);

		if (!inPlace && File.Exists(targetFull) && !request.Force)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-file-exists",
				$"'{targetRelative}' already exists in the project. Pass --force to overwrite it.",
				warnings: warnings);
		}

		var placement = inPlace
			? BundledIconPackPlacement.InPlace
			: IsInside(sourceFull, project.Root) && !request.Copy
				? BundledIconPackPlacement.Moved
				: BundledIconPackPlacement.Copied;

		var previousFull = existing is not null && IsContainedRelative(existing.Path, project.Root)
			? Resolve(project.Root, existing.Path)
			: null;

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(targetFull)!);

			if (placement != BundledIconPackPlacement.InPlace)
			{
				File.Copy(sourceFull, targetFull, overwrite: true);
			}

			var entry = new JsonObject { ["key"] = key, ["path"] = targetRelative };
			var array = EnsureArray(project.Document);

			if (existingIndex >= 0)
			{
				array[existingIndex] = entry;
			}
			else
			{
				array.Add(entry);
			}

			Save(project);

			if (placement == BundledIconPackPlacement.Moved)
			{
				File.Delete(sourceFull);
			}

			if (previousFull is not null &&
				!string.Equals(previousFull, targetFull, _pathComparison) &&
				!string.Equals(previousFull, sourceFull, _pathComparison) &&
				File.Exists(previousFull))
			{
				File.Delete(previousFull);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable, "write-failed", ex.Message, warnings: warnings);
		}

		return new BundledIconPackOutcome
		{
			Key = key,
			TargetPath = targetRelative,
			PackName = info.Name,
			IconCount = info.Icons.Count,
			Placement = placement,
			MovedFrom = placement == BundledIconPackPlacement.Moved
				? Path.GetRelativePath(project.Root, sourceFull).Replace(Path.DirectorySeparatorChar, '/')
				: null,
			Replaced = existing is not null,
			Warnings = warnings
		};
	}

	public static BundledIconPackOutcome Remove(string sourceDirectory, string manifestPath, string key)
	{
		if (Load(sourceDirectory, manifestPath, out var project) is { } loadFailure)
		{
			return loadFailure;
		}

		var entries = ReadEntries(project.Document, out var entriesFailure);
		if (entriesFailure is not null)
		{
			return entriesFailure;
		}

		var index = entries.FindIndex(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));
		if (index < 0)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"icon-pack-not-declared",
				$"The manifest bundles no icon pack under the key '{key}'.");
		}

		var removed = entries[index];
		var deleted = false;

		try
		{
			var array = EnsureArray(project.Document);
			array.RemoveAt(index);

			if (array.Count == 0)
			{
				project.Document.Remove(PropertyName);
			}

			Save(project);

			if (IsContainedRelative(removed.Path, project.Root) && Resolve(project.Root, removed.Path) is var full &&
				File.Exists(full))
			{
				File.Delete(full);
				deleted = true;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable, "write-failed", ex.Message);
		}

		return new BundledIconPackOutcome { Key = removed.Key, TargetPath = removed.Path, FileDeleted = deleted };
	}

	public static BundledIconPackOutcome? Load(string sourceDirectory, string manifestPath, out ProjectManifest project)
	{
		project = null!;

		if (!Directory.Exists(sourceDirectory))
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable,
				"source-not-found",
				$"No project directory at '{CliText.DisplayPath(sourceDirectory)}'.");
		}

		if (!File.Exists(manifestPath))
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable,
				"manifest-not-found",
				$"No manifest at '{CliText.DisplayPath(manifestPath)}'.");
		}

		string json;
		try
		{
			json = File.ReadAllText(manifestPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return BundledIconPackOutcome.Fail(ExitCode.InputUnreadable, "manifest-not-found", ex.Message);
		}

		try
		{
			var document = JsonNode.Parse(json, documentOptions: _readOptions) as JsonObject;
			var manifest = JsonSerializer.Deserialize<PluginManifest>(json, PluginManifestJson.Options);

			if (document is null || manifest is null)
			{
				return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
					"malformed",
					$"'{CliText.DisplayPath(manifestPath)}' is not a JSON object.");
			}

			project = new ProjectManifest
			{
				Root = Path.GetFullPath(sourceDirectory),
				ManifestPath = manifestPath,
				Document = document,
				Manifest = manifest,
				EndsWithNewline = json.EndsWith('\n'),
				NewLine = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
				WriteOptions = WriteOptionsMatching(json)
			};

			return null;
		}
		catch (JsonException ex)
		{
			return BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"malformed",
				$"'{CliText.DisplayPath(manifestPath)}' could not be read as a manifest: {ex.Message}");
		}
	}

	private static string? DescribeNameProblems(IconPackArchiveInfo info)
	{
		var lines = new List<string>();

		if (info.DuplicateNames.Count > 0)
		{
			lines.Add("  Used by more than one icon (names are compared case-insensitively): " +
				string.Join(", ", info.DuplicateNames.Select(name => $"'{name}'")));
		}

		if (info.UnusableNames.Count > 0)
		{
			lines.Add("  Not usable as a name (blank, surrounding whitespace, '/', control characters or longer than " +
				$"{IconPackArchive.MaxIconNameLength} characters): " +
				string.Join(", ", info.UnusableNames.Select(name => $"'{name}'")));
		}

		var withoutMaster = info.Icons.Where(icon => !icon.HasMaster).Select(icon => icon.Name).ToList();
		if (withoutMaster.Count > 0)
		{
			lines.Add("  Without a master image: " + string.Join(", ", withoutMaster.Select(name => $"'{name}'")));
		}

		return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
	}

	private static List<PluginBundledIconPack> ReadEntries(JsonObject document, out BundledIconPackOutcome? failure)
	{
		failure = null;

		if (!document.TryGetPropertyValue(PropertyName, out var node) || node is null)
		{
			return [];
		}

		if (node is not JsonArray array)
		{
			failure = BundledIconPackOutcome.Fail(ExitCode.SubjectInvalid,
				"invalid-bundled-icon-pack",
				$"'{PropertyName}' in the manifest is not an array.");
			return [];
		}

		var entries = new List<PluginBundledIconPack>();
		foreach (var item in array)
		{
			entries.Add(new PluginBundledIconPack
			{
				Key = (item as JsonObject)?["key"] is JsonValue key && key.TryGetValue<string>(out var k) ? k : string.Empty,
				Path = (item as JsonObject)?["path"] is JsonValue path && path.TryGetValue<string>(out var p) ? p : string.Empty
			});
		}

		return entries;
	}

	private static JsonArray EnsureArray(JsonObject document)
	{
		if (document[PropertyName] is JsonArray array)
		{
			return array;
		}

		array = [];
		document[PropertyName] = array;
		return array;
	}

	private static JsonSerializerOptions WriteOptionsMatching(string json)
	{
		var indent = json.Split('\n')
			.Skip(1)
			.Select(line => line[..(line.Length - line.TrimStart(' ', '\t').Length)])
			.FirstOrDefault(whitespace => whitespace.Length > 0);

		return new JsonSerializerOptions
		{
			WriteIndented = true,
			IndentCharacter = indent?.StartsWith('\t') == true ? '\t' : ' ',
			IndentSize = indent is null ? 2 : Math.Clamp(indent.TakeWhile(c => c == indent[0]).Count(), 1, 127),
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};
	}

	private static void Save(ProjectManifest project)
	{
		var json = project.Document.ToJsonString(project.WriteOptions);
		File.WriteAllText(project.ManifestPath, project.EndsWithNewline ? json + project.NewLine : json);
	}

	private static string Resolve(string root, string relativePath)
		=> Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

	private static bool IsContainedRelative(string relativePath, string root)
		=> !string.IsNullOrEmpty(relativePath) && !Path.IsPathRooted(relativePath) && IsInside(Resolve(root, relativePath), root);

	private static bool IsInside(string fullPath, string root)
	{
		var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
		return fullPath.StartsWith(prefix, _pathComparison);
	}
}
