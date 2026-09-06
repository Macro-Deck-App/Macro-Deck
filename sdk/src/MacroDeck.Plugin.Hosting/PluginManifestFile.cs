using System.Text.Json;

namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// The subset of a plugin's <c>manifest.json</c> the SDK cares about at startup: identity and icon.
/// This type deliberately does not model entrypoints, files, permissions, signature and the rest - those
/// are the host's install-time and launch-time concerns, not the running plugin's.
/// </summary>
internal sealed record PluginManifestFile
{
	public int? ManifestVersion { get; init; }

	public string? Id { get; init; }

	public string? Name { get; init; }

	public string? Version { get; init; }

	public string? Description { get; init; }

	public string? Icon { get; init; }
}

/// <summary>
/// Reads <c>manifest.json</c> from a plugin's content root.
///
/// <para>
/// This is a small, deliberately partial reader, not a reuse of the host's
/// <c>MacroDeckHost.Application.Plugins.Runtime.PluginManifest</c> reader: the SDK cannot reference
/// <c>MacroDeckHost.Application</c>, and that record must not move into the protocol package either -
/// the protocol package is the wire contract and would end up carrying entrypoints, digests, signatures
/// and install-time semantics it has no business knowing about. A parity test
/// (<c>MacroDeckHost.Tests.PluginContractTests</c>) pins this reader and the host's reader to the same
/// answers for the fields both model.
/// </para>
/// </summary>
internal static class PluginManifestFileReader
{
	public const string FileName = "manifest.json";

	public const int SupportedManifestVersion = 1;

	/// <summary>Pinned to <c>PluginArtifactLimits.MaxManifestBytes</c>, which this package cannot
	/// reference (see the remarks above); the parity test keeps the two honest. A self-contained plugin's
	/// recomputed <c>files[]</c> is what makes a manifest large, so the bound has to match the packer's,
	/// while still refusing to slurp an arbitrarily huge file.</summary>
	private const int MaxManifestBytes = 8 * 1024 * 1024;

	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
		// UnmappedMemberHandling is deliberately left at its default (Skip), not Disallow: a real plugin
		// manifest always carries entrypoints, files, publisher and more, none of which this record
		// models. Rejecting them would fail on every real manifest and defeat the forward-compatibility
		// contract the published schema documents.
	};

	public static PluginManifestFile? Read(string contentRootPath, List<string> problems)
	{
		var manifestPath = Path.Combine(contentRootPath, FileName);

		FileInfo fileInfo;
		try
		{
			fileInfo = new FileInfo(manifestPath);
			if (!fileInfo.Exists)
			{
				problems.Add($"No manifest at '{manifestPath}'. A plugin declares its id, name, version, " +
					"description and icon there.");
				return null;
			}

			if (fileInfo.Length > MaxManifestBytes)
			{
				problems.Add($"The manifest at '{manifestPath}' exceeds the {MaxManifestBytes}-byte limit.");
				return null;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			problems.Add($"The manifest at '{manifestPath}' could not be read: {ex.Message}");
			return null;
		}

		string json;
		try
		{
			json = File.ReadAllText(manifestPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			problems.Add($"The manifest at '{manifestPath}' could not be read: {ex.Message}");
			return null;
		}

		PluginManifestFile? manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<PluginManifestFile>(json, _jsonOptions);
		}
		catch (JsonException ex)
		{
			problems.Add($"The manifest at '{manifestPath}' is not valid JSON: {ex.Message}");
			return null;
		}

		if (manifest is null)
		{
			problems.Add($"The manifest at '{manifestPath}' deserialized to null.");
			return null;
		}

		return manifest;
	}

	/// <summary>
	/// Relative, forward-slash only, no <c>..</c> segment, not rooted, non-empty. The identical rule to
	/// the host reader's <c>IsSafeRelativeForwardSlashPath</c> - deliberately duplicated rather than
	/// shared, since the two live in packages that cannot reference each other. A parity test pins both
	/// readers to the same answers for the same inputs.
	/// </summary>
	public static bool IsSafeRelativeIconPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\'))
		{
			return false;
		}

		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return segments.Length > 0 && !segments.Any(segment => segment == "..");
	}
}
