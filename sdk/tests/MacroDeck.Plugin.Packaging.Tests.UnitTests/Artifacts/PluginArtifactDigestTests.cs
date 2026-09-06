using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts;

[TestFixture]
internal sealed class PluginArtifactDigestTests
{
	private static readonly JsonSerializerOptions _options = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private static PluginManifest Deserialize(string json)
	{
		var manifest = JsonSerializer.Deserialize<PluginManifest>(json, _options);
		Assert.That(manifest, Is.Not.Null);
		return manifest!;
	}

	private const string OrderedJson = """
									   {
									   	"manifestVersion": 1,
									   	"id": "com.suchbyte.test-plugin",
									   	"name": "Test",
									   	"version": "1.0.0",
									   	"entrypoints": { "osx-arm64": { "executable": "Test" } },
									   	"files": [
									   		{ "path": "b.dll", "sha256": "sha256:bb00000000000000000000000000000000000000000000000000000000000000", "size": 2 },
									   		{ "path": "a.dll", "sha256": "sha256:aa00000000000000000000000000000000000000000000000000000000000000", "size": 1 }
									   	]
									   }
									   """;

	private const string ReorderedJson = """
										 {
										 	"files": [
										 		{ "size": 1, "sha256": "sha256:aa00000000000000000000000000000000000000000000000000000000000000", "path": "a.dll" },
										 		{ "size": 2, "sha256": "sha256:bb00000000000000000000000000000000000000000000000000000000000000", "path": "b.dll" }
										 	],
										 	"version": "1.0.0",
										 	"entrypoints": { "osx-arm64": { "executable": "Test" } },
										 	"name": "Test",
										 	"id": "com.suchbyte.test-plugin",
										 	"manifestVersion": 1
										 }
										 """;

	/// <summary>
	/// The digest is what a signature covers, so it has to depend on the manifest's meaning and nothing
	/// else. Property order, whitespace and the order of the file list are all presentation.
	/// </summary>
	[Test]
	public void Reordering_the_document_does_not_change_the_digest()
	{
		var first = PluginArtifactDigest.Compute(Deserialize(OrderedJson));
		var second = PluginArtifactDigest.Compute(Deserialize(ReorderedJson));

		Assert.That(first, Is.EqualTo(second));
	}

	[Test]
	public void Recomputing_the_digest_for_the_same_manifest_is_stable()
	{
		var manifest = Deserialize(OrderedJson);
		var first = PluginArtifactDigest.Compute(manifest);
		var second = PluginArtifactDigest.Compute(manifest);

		Assert.That(first, Is.EqualTo(second));
	}

	/// <summary>Without this, a digest covering only the id and version would let an attacker swap the
	/// payload underneath a signature that still verifies.</summary>
	[Test]
	public void Changing_a_declared_file_digest_changes_the_artifact_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var tampered = manifest with
		{
			Files =
			[
				manifest.Files![0] with
				{
					Sha256 = "sha256:cc00000000000000000000000000000000000000000000000000000000000000"
				},
				manifest.Files[1]
			]
		};

		Assert.That(PluginArtifactDigest.Compute(tampered),
			Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
	}

	[Test]
	public void Changing_a_declared_file_size_changes_the_artifact_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var tampered = manifest with
		{
			Files = [manifest.Files![0] with { Size = 999 }, manifest.Files[1]]
		};

		Assert.That(PluginArtifactDigest.Compute(tampered),
			Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
	}

	[Test]
	public void The_plugin_id_and_version_are_bound_into_the_digest()
	{
		var manifest = Deserialize(OrderedJson);

		Assert.Multiple(() =>
		{
			Assert.That(PluginArtifactDigest.Compute(manifest with { Id = "com.suchbyte.other" }),
				Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
			Assert.That(PluginArtifactDigest.Compute(manifest with { Version = "2.0.0" }),
				Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
		});
	}

	[Test]
	public void A_manifest_with_no_declared_files_still_produces_a_bound_document()
	{
		var manifest = Deserialize(OrderedJson) with { Files = null };
		var text = Encoding.UTF8.GetString(PluginArtifactDigest.Compute(manifest));

		Assert.Multiple(() =>
		{
			Assert.That(text, Does.Contain("com.suchbyte.test-plugin"));
			Assert.That(text, Does.Contain("1.0.0"));
		});
	}

	/// <summary>
	/// A pinned vector against the layout documented in ADR 0029 and the plugin hosting guide. Changing
	/// this byte format invalidates every signature ever issued, so if this fails, either the change is
	/// wrong or the document's own version prefix has to change with it.
	/// </summary>
	[Test]
	public void The_digest_document_matches_the_documented_layout()
	{
		var manifest = Deserialize(OrderedJson);
		var expected =
			"macro-deck-plugin/1\n" +
			"com.suchbyte.test-plugin\n" +
			"1.0.0\n" +
			"e\tosx-arm64\tSelfContained\t\tTest\n" +
			"f\ta.dll\tsha256:aa00000000000000000000000000000000000000000000000000000000000000\t1\n" +
			"f\tb.dll\tsha256:bb00000000000000000000000000000000000000000000000000000000000000\t2\n";

		Assert.That(Encoding.UTF8.GetString(PluginArtifactDigest.Compute(manifest)),
			Is.EqualTo(expected));
	}

	/// <summary>
	/// The pinned cross-component vectors in <c>sdk/tests/fixtures/signing-vectors/plugin-digest-vectors.json</c>,
	/// shared with <c>MacroDeck.Signing.Tests.UnitTests</c>' own copy - run here through the typed
	/// <see cref="PluginManifest" /> model with the production <see cref="PluginManifestJson.Options" />
	/// serializer, matching how a real manifest is read.
	/// </summary>
	[TestCaseSource(nameof(_pluginDigestVectors))]
	public void Pinned_digest_vector_matches(PluginDigestVector vector)
	{
		var manifest = JsonSerializer.Deserialize<PluginManifest>(vector.ManifestJson, PluginManifestJson.Options)!;

		var actual = Encoding.UTF8.GetString(PluginArtifactDigest.Compute(manifest));

		Assert.That(actual, Is.EqualTo(vector.DigestUtf8), $"vector '{vector.Name}'");
	}

	private static readonly IReadOnlyList<PluginDigestVector> _pluginDigestVectors = PluginDigestVector.Load();

	/// <summary>
	/// The entrypoints decide which file runs and with which arguments. A digest covering only the payload
	/// hashes would let a signed artifact be repackaged to launch a different signed file, or the same one
	/// with attacker-chosen arguments, while the signature still verified.
	/// </summary>
	[Test]
	public void Changing_an_entrypoint_changes_the_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var repointed = manifest with
		{
			Entrypoints = new Dictionary<string, PluginEntrypoint>(StringComparer.Ordinal)
			{
				["osx-arm64"] = new() { Executable = "Other" }
			}
		};

		var rearmed = manifest with
		{
			Entrypoints = new Dictionary<string, PluginEntrypoint>(StringComparer.Ordinal)
			{
				["osx-arm64"] = new() { Executable = "Test", Arguments = ["--evil"] }
			}
		};

		Assert.Multiple(() =>
		{
			Assert.That(PluginArtifactDigest.Compute(repointed),
				Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
			Assert.That(PluginArtifactDigest.Compute(rearmed),
				Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
		});
	}

	/// <summary>Permissions are what the artifact claims it may reach, so a signature has to cover
	/// them.</summary>
	[Test]
	public void Adding_a_permission_changes_the_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var escalated = manifest with { Permissions = ["host:scripts"] };

		Assert.That(PluginArtifactDigest.Compute(escalated),
			Is.Not.EqualTo(PluginArtifactDigest.Compute(manifest)));
	}

	/// <summary>Reordering declarations is presentation, not meaning.</summary>
	[Test]
	public void Reordering_permissions_does_not_change_the_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var first = manifest with { Permissions = ["host:deck", "host:scripts"] };
		var second = manifest with { Permissions = ["host:scripts", "host:deck"] };

		Assert.That(PluginArtifactDigest.Compute(first),
			Is.EqualTo(PluginArtifactDigest.Compute(second)));
	}

	/// <summary>
	/// Declaring an icon must invalidate no signature already issued. Two different icon values are used
	/// deliberately: an implementation that appended a constant marker for "icon present" rather than the
	/// icon's own bytes would still pass with only one value tested.
	/// </summary>
	[Test]
	public void Declaring_an_icon_does_not_change_the_artifact_digest()
	{
		var manifest = Deserialize(OrderedJson);
		var withFirstIcon = manifest with { Icon = "assets/icon.png" };
		var withSecondIcon = manifest with { Icon = "other.png" };

		var baseline = PluginArtifactDigest.Compute(manifest);

		Assert.Multiple(() =>
		{
			Assert.That(PluginArtifactDigest.Compute(withFirstIcon), Is.EqualTo(baseline));
			Assert.That(PluginArtifactDigest.Compute(withSecondIcon), Is.EqualTo(baseline));
		});
	}
}
