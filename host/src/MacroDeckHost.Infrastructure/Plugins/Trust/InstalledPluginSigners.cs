using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

// Unverified by design: this only labels an installed version in the Store. Admission is decided by the
// trust evaluator, which verifies the same files.
public sealed class InstalledPluginSigners : IInstalledPluginSigners
{
	public PluginSigners? Read(InstalledPluginVersion version)
	{
		ArgumentNullException.ThrowIfNull(version);

		var path = Path.Combine(version.VersionDirectory, PluginArtifactFiles.CertificateFileName);
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			using var document = JsonDocument.Parse(File.ReadAllBytes(path));
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("certificateId", out var certificateId) ||
				certificateId.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			var issuer = root.TryGetProperty("issuer", out var issuerId) && issuerId.ValueKind == JsonValueKind.String
				? issuerId.GetString()
				: null;
			return new PluginSigners(certificateId.GetString()!, issuer);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return null;
		}
	}
}
