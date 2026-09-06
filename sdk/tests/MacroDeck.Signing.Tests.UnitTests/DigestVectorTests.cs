using System.Text;
using System.Text.Json.Nodes;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Signing.Packages;

namespace MacroDeck.Signing.Tests.UnitTests;

/// <summary>
/// The pinned cross-component vectors in <c>sdk/tests/fixtures/signing-vectors/</c>, run through each
/// format's own digest implementation. <see cref="MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts.PluginArtifactDigestTests" />
/// runs the same plugin vectors independently, so a regression in either implementation is caught from both
/// sides.
/// </summary>
[TestFixture]
internal sealed class DigestVectorTests
{
	private static IEnumerable<DigestVector> PluginVectors => SigningVectorFixtures.Load("plugin-digest-vectors.json");

	private static IEnumerable<DigestVector> IconPackVectors =>
		SigningVectorFixtures.Load("iconpack-digest-vectors.json");

	private static IEnumerable<DigestVector> PortableVectors =>
		SigningVectorFixtures.Load("portable-digest-vectors.json");

	[TestCaseSource(nameof(PluginVectors))]
	public void Plugin_vector_digest_matches(DigestVector vector)
	{
		var manifest
			= System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(vector.ManifestJson,
				PluginManifestJson.Options)!;

		var actual = Encoding.UTF8.GetString(PluginArtifactDigest.Compute(manifest));

		Assert.That(actual, Is.EqualTo(vector.DigestUtf8), $"vector '{vector.Name}'");
	}

	[TestCaseSource(nameof(IconPackVectors))]
	public void IconPack_vector_digest_matches(DigestVector vector)
	{
		var manifest = (JsonObject)JsonNode.Parse(vector.ManifestJson)!;

		var result = IconPackDigest.Compute(manifest);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, $"vector '{vector.Name}'");
			Assert.That(Encoding.UTF8.GetString(result.Digest!),
				Is.EqualTo(vector.DigestUtf8),
				$"vector '{vector.Name}'");
		});
	}

	[TestCaseSource(nameof(PortableVectors))]
	public void Portable_vector_digest_matches(DigestVector vector)
	{
		var manifest = (JsonObject)JsonNode.Parse(vector.ManifestJson)!;

		var result = PortablePackageDigest.Compute(manifest);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, $"vector '{vector.Name}'");
			Assert.That(Encoding.UTF8.GetString(result.Digest!),
				Is.EqualTo(vector.DigestUtf8),
				$"vector '{vector.Name}'");
		});
	}
}
