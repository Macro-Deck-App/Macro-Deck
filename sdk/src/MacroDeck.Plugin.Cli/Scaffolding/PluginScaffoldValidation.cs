using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Cli.Scaffolding;

/// <summary>Pure validators shared by the wizard's re-prompt loop and the non-interactive flag path, so the
/// two can never quietly accept different things.</summary>
internal static class PluginScaffoldValidation
{
	public static CliDiagnostic? ValidatePluginId(string id)
		=> PluginId.TryValidate(id, out var error) ? null : new CliDiagnostic("invalid-plugin-id", error!);

	public static CliDiagnostic? ValidateUrl(string value, string fieldName)
		=> PluginManifestUrls.IsAbsoluteHttpUrl(value)
			? null
			: new CliDiagnostic($"invalid-{fieldName}-url", $"'{value}' must be an absolute http or https URL.");

	/// <summary>Checks every token against the known RID vocabulary, then - only once every token is known -
	/// that at least one survived. Order matters: an unknown token is always reported as
	/// <c>unknown-platform</c>, never masked by an empty-selection message.</summary>
	public static CliDiagnostic? ValidatePlatforms(IReadOnlyList<string> platforms)
	{
		foreach (var platform in platforms)
		{
			if (!PluginScaffoldDefaults.KnownPlatforms.Contains(platform, StringComparer.Ordinal))
			{
				return new CliDiagnostic("unknown-platform",
					$"'{platform}' is not a supported target platform. Supported: " +
					$"{string.Join(", ", PluginScaffoldDefaults.KnownPlatforms)}.");
			}
		}

		return platforms.Count == 0
			? new CliDiagnostic("no-platform-selected", "At least one platform must be selected.")
			: null;
	}

	public static CliDiagnostic? ValidateOutputDirectory(string outputDirectory)
	{
		// Checked before Directory.Exists, which is false for a regular file: without this the scaffolder
		// would call Directory.CreateDirectory on it and surface an unhandled IOException instead.
		if (File.Exists(outputDirectory))
		{
			return new CliDiagnostic("output-exists", $"'{CliText.DisplayPath(outputDirectory)}' is a file.");
		}

		if (!Directory.Exists(outputDirectory) || !Directory.EnumerateFileSystemEntries(outputDirectory).Any())
		{
			return null;
		}

		return new CliDiagnostic("output-exists",
			$"'{CliText.DisplayPath(outputDirectory)}' already exists and is not empty.");
	}
}
