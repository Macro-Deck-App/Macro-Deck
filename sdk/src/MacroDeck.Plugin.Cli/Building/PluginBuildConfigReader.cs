using System.Text.Json;

namespace MacroDeck.Plugin.Cli.Building;

internal readonly record struct PluginBuildConfigReadResult(
	IReadOnlyDictionary<string, PluginBuildTarget>? Targets,
	IReadOnlyList<string> Include,
	PluginBuildFailureReason? FailureReason,
	string? FailureMessage)
{
	public static PluginBuildConfigReadResult Ok(IReadOnlyDictionary<string, PluginBuildTarget> targets,
		IReadOnlyList<string> include)
		=> new(targets, include, null, null);

	public static PluginBuildConfigReadResult Fail(PluginBuildFailureReason reason, string message)
		=> new(null, [], reason, message);
}

/// <summary>
/// Reads <c>macrodeck-build.json</c>, the developer build configuration <c>macrodeck-plugin new</c> writes
/// and <c>build</c> consumes. Read through <see cref="JsonDocument" /> rather than a typed model on
/// purpose: the format is additive, so a property a newer CLI writes must be ignored here rather than
/// making an older CLI refuse the file.
/// </summary>
internal static class PluginBuildConfigReader
{
	public const string FileName = "macrodeck-build.json";

	/// <summary>The only version this tool understands. An unknown version is refused rather than guessed
	/// at: a forward version may attach meaning to properties this reader would silently drop.</summary>
	private const int SupportedVersion = 1;

	/// <param name="sourceRoot">The project root every relative path in the config is resolved against and
	/// must stay inside.</param>
	public static PluginBuildConfigReadResult Read(string buildConfigPath, string sourceRoot)
	{
		if (!File.Exists(buildConfigPath))
		{
			return PluginBuildConfigReadResult.Fail(PluginBuildFailureReason.BuildConfigNotFound,
				$"No build configuration at '{CliText.DisplayPath(buildConfigPath)}'. " +
				$"'{FileName}' describes how each runtime identifier is built; 'macrodeck-plugin new' writes one.");
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(File.ReadAllText(buildConfigPath));
		}
		catch (JsonException ex)
		{
			return PluginBuildConfigReadResult.Fail(PluginBuildFailureReason.BuildConfigMalformed,
				$"'{CliText.DisplayPath(buildConfigPath)}' is not valid JSON: {ex.Message}");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return PluginBuildConfigReadResult.Fail(PluginBuildFailureReason.BuildConfigNotFound,
				$"Could not read '{CliText.DisplayPath(buildConfigPath)}': {ex.Message}");
		}

		using (document)
		{
			return ReadDocument(document.RootElement, sourceRoot);
		}
	}

	private static PluginBuildConfigReadResult ReadDocument(JsonElement root, string sourceRoot)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return Invalid("The build configuration must be a JSON object.");
		}

		if (!root.TryGetProperty("version", out var version) ||
			version.ValueKind != JsonValueKind.Number ||
			!version.TryGetInt32(out var versionNumber))
		{
			return Invalid("The build configuration must declare a numeric 'version'.");
		}

		if (versionNumber != SupportedVersion)
		{
			return Invalid($"The build configuration declares version {versionNumber}; " +
				$"this tool understands version {SupportedVersion}.");
		}

		if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
		{
			return Invalid("The build configuration must declare a 'targets' object.");
		}

		var parsed = new Dictionary<string, PluginBuildTarget>(StringComparer.Ordinal);

		foreach (var property in targets.EnumerateObject())
		{
			var rid = property.Name;

			if (property.Value.ValueKind != JsonValueKind.Object)
			{
				return Invalid($"Target '{rid}' must be an object.");
			}

			var target = ReadTarget(rid, property.Value, sourceRoot, out var failure);
			if (target is null)
			{
				return Invalid(failure!);
			}

			parsed[rid] = target;
		}

		if (parsed.Count == 0)
		{
			return Invalid("The build configuration declares no targets.");
		}

		var include = new List<string>();

		if (root.TryGetProperty("include", out var includeElement) && includeElement.ValueKind != JsonValueKind.Null)
		{
			if (includeElement.ValueKind != JsonValueKind.Array)
			{
				return Invalid("The build configuration has an 'include' value that is not an array.");
			}

			foreach (var entry in includeElement.EnumerateArray())
			{
				if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString()))
				{
					return Invalid("Every 'include' entry must be a non-empty string.");
				}

				if (!IsContainedRelativePath(entry.GetString()!, sourceRoot))
				{
					return Invalid($"'include' names a path outside the project directory: '{entry.GetString()}'.");
				}

				var resolved = Path.Combine(sourceRoot, entry.GetString()!);

				if (!File.Exists(resolved) && !Directory.Exists(resolved))
				{
					return Invalid($"'include' names a path that does not exist: '{entry.GetString()}'.");
				}

				include.Add(entry.GetString()!);
			}
		}

		return PluginBuildConfigReadResult.Ok(parsed, include);
	}

	private static PluginBuildTarget? ReadTarget(string rid,
		JsonElement element,
		string sourceRoot,
		out string? failure)
	{
		failure = null;

		if (!element.TryGetProperty("executable", out var executable) ||
			executable.ValueKind != JsonValueKind.String ||
			string.IsNullOrWhiteSpace(executable.GetString()))
		{
			failure = $"Target '{rid}' must declare a non-empty 'executable'.";
			return null;
		}

		var arguments = new List<string>();

		if (element.TryGetProperty("arguments", out var argumentsElement))
		{
			if (argumentsElement.ValueKind != JsonValueKind.Array)
			{
				failure = $"Target '{rid}' has an 'arguments' value that is not an array.";
				return null;
			}

			var index = 0;
			foreach (var argument in argumentsElement.EnumerateArray())
			{
				// An empty argument is deliberately allowed: '-p:Something=' is a legitimate vector element,
				// and this format is not .NET-specific.
				if (argument.ValueKind != JsonValueKind.String)
				{
					failure = $"Target '{rid}' argument {index} is not a string.";
					return null;
				}

				arguments.Add(argument.GetString()!);
				index++;
			}
		}

		if (!element.TryGetProperty("output", out var output) ||
			output.ValueKind != JsonValueKind.String ||
			string.IsNullOrWhiteSpace(output.GetString()))
		{
			failure = $"Target '{rid}' must declare a non-empty 'output'.";
			return null;
		}

		if (!IsContainedRelativePath(output.GetString()!, sourceRoot))
		{
			failure = $"Target '{rid}' declares an 'output' outside the project directory: '{output.GetString()}'.";
			return null;
		}

		string? workingDirectory = null;

		if (element.TryGetProperty("workingDirectory", out var workingDirectoryElement) &&
			workingDirectoryElement.ValueKind != JsonValueKind.Null)
		{
			if (workingDirectoryElement.ValueKind != JsonValueKind.String ||
				string.IsNullOrWhiteSpace(workingDirectoryElement.GetString()))
			{
				failure = $"Target '{rid}' has a 'workingDirectory' value that is not a non-empty string.";
				return null;
			}

			workingDirectory = workingDirectoryElement.GetString();

			if (!IsContainedRelativePath(workingDirectory!, sourceRoot))
			{
				failure = $"Target '{rid}' declares a 'workingDirectory' outside the project directory: " +
					$"'{workingDirectory}'.";
				return null;
			}
		}

		return new PluginBuildTarget
		{
			Rid = rid,
			Executable = executable.GetString()!,
			Arguments = arguments,
			Output = output.GetString()!,
			WorkingDirectory = workingDirectory
		};
	}

	/// <summary>The same containment judgement the packer applies to payload entries: relative, no escape
	/// above the project root. A build recipe must never be able to read or write outside the project it
	/// belongs to.</summary>
	internal static bool IsContainedRelativePath(string path, string sourceRoot)
	{
		if (Path.IsPathRooted(path))
		{
			return false;
		}

		var root = Path.GetFullPath(sourceRoot);
		var resolved = Path.GetFullPath(Path.Combine(root, path));

		return resolved.Equals(root, StringComparison.Ordinal) ||
			resolved.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar,
				StringComparison.Ordinal);
	}

	private static PluginBuildConfigReadResult Invalid(string message)
		=> PluginBuildConfigReadResult.Fail(PluginBuildFailureReason.BuildConfigInvalid, message);
}
