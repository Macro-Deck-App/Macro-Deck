using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// Translates a <see cref="PluginInstallError" /> from <see cref="IPluginArtifactReader" /> into a
/// <see cref="CliDiagnostic" /> a plugin author can act on. <see cref="PluginInstallError.InvalidArchive" />
/// gets special handling: the raw .NET ZIP exception ("End of Central Directory record could not be
/// found") names neither the file nor the most likely mistake - pointing <c>--artifact</c> at a
/// <c>manifest.json</c> instead of a packed <c>.macroDeckPlugin</c>. Every other error passes through with
/// its existing message untouched.
/// </summary>
internal static class ArtifactErrorText
{
	public static CliDiagnostic Describe(string artifactPath, PluginInstallError error, string? message)
	{
		if (error == PluginInstallError.InvalidArchive)
		{
			var displayPath = CliText.DisplayPath(artifactPath);
			var hint = string.Equals(Path.GetFileName(artifactPath),
				PluginArtifactFiles.ManifestFileName,
				StringComparison.OrdinalIgnoreCase)
				? " Did you mean validate --manifest?"
				: string.Empty;

			return new CliDiagnostic("not-an-artifact",
				$"'{displayPath}' is not a .macroDeckPlugin artifact (not a ZIP archive).{hint}");
		}

		return new CliDiagnostic(CliText.KebabCase(error.ToString()), message ?? "The artifact is invalid.");
	}
}
