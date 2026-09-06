using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using MacroDeck.Signing.Packages;
using MacroDeck.Signing.TestSupport;
using MacroDeckHost.Tests.UnitTests.Plugins.Installation;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Trust;

/// <summary>Builds real, cryptographically signed <c>.macroDeckPlugin</c> archives for host trust-enforcement
/// tests, anchored to <see cref="TestPki.Root" /> - never <see cref="MacroDeckRootKey" />. Every host test
/// that exercises real verification passes <c>TestPki.Root.PublicKey</c> as the evaluator's root override.
/// Built with <see cref="ManifestJson" /> - not <see cref="PackageArchiveFixtures.CreatePluginArchive" /> -
/// so the manifest declares an entrypoint for every runtime identifier a test machine might run under,
/// letting a signed fixture actually launch under the fake supervisor.</summary>
internal static class SignedPluginArtifacts
{
	private static readonly PluginManifestReader _manifestReader = new();

	public static async Task<string> CreateSignedAsync(string directory,
		string id,
		string version,
		string content = "binary",
		string fileName = "plugin.macroDeckPlugin",
		(byte[] PrivateKey, byte[] PublicKey)? signingRoot = null,
		IReadOnlyList<string>? keyUsage = null,
		DateTimeOffset? notBefore = null,
		DateTimeOffset? notAfter = null)
	{
		var digest = PluginArtifactBuilder.Sha256Of(Encoding.UTF8.GetBytes(content));
		var filesBlock =
			$$"""
			  "files": [
			  	{
			  		"path": "{{ManifestJson.EntrypointExecutable}}",
			  		"sha256": "{{digest}}",
			  		"size": {{Encoding.UTF8.GetByteCount(content)}}
			  	}
			  ]
			  """;

		var unsignedPath = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, id, filesBlock))
			.WithFile(ManifestJson.EntrypointExecutable, content)
			.WriteTo(directory, $"unsigned-{Guid.NewGuid():N}.macroDeckPlugin");

		var issued = TestPki.IssueCertificate(keyUsage: keyUsage,
			notBefore: notBefore,
			notAfter: notAfter,
			signingRoot: signingRoot);

		// Deserialized directly rather than routed through TestPki.VerifyChain: that helper throws on a
		// certificate that does not verify, which is exactly what a wrong-purpose or not-yet-valid fixture
		// needs to build - the point is for verification to fail later, inside the host under test, not
		// here while assembling the fixture.
		var certificate
			= JsonSerializer.Deserialize<SigningCertificate>(issued.CertificateBytes, SigningJson.Options) ??
			throw new InvalidOperationException("Could not deserialize the issued test certificate.");

		var materialResult = SigningMaterial.Create(issued.PrivateKey, certificate);
		if (!materialResult.Success)
		{
			throw new InvalidOperationException($"Could not build signing material: {materialResult.Error}");
		}

		using var signer = materialResult.Material!;
		var outputPath = Path.Combine(directory, fileName);
		var result = await PackageSigner.SignAsync(unsignedPath,
			outputPath,
			signer,
			issued.CertificateBytes,
			issued.CertificateSignatureBytes,
			_manifestReader);

		if (!result.Success)
		{
			throw new InvalidOperationException($"Could not sign fixture: {result.Error} {result.Message}");
		}

		File.Delete(unsignedPath);
		return outputPath;
	}
}
