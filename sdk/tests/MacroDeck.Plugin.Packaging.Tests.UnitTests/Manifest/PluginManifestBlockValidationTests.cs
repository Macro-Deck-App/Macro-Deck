using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

/// <summary>
/// Validation of the manifest blocks #415 added. The rule throughout is that a shape violation is a
/// rejection, while the pre-existing supervisor settings keep being clamped rather than refused - an
/// out-of-range timeout is a foot-gun, a malformed dependency is a document that cannot mean anything.
/// </summary>
[TestFixture]
internal sealed class PluginManifestBlockValidationTests
{
	private const string PluginId = "com.suchbyte.test-plugin";

	private static readonly string[] _singleUnknownPermission = ["totally.made.up"];

	private static readonly string[] _threeLanguages = ["en", "zh-Hant-TW", "qya"];

	private readonly PluginManifestReader _reader = new();

	private PluginManifestReadResult Read(string? extraBlocks, string? entrypoints = null)
	{
		var entrypointBlock = entrypoints ??
			"""
			"entrypoints": { "linux-x64": { "executable": "TestPlugin" } }
			""";

		var extras = extraBlocks is null ? string.Empty : "," + extraBlocks;

		var json = $$"""
					 {
					 	"manifestVersion": 1,
					 	"id": "{{PluginId}}",
					 	"name": "Test Plugin",
					 	"version": "1.0.0",
					 	{{entrypointBlock}}{{extras}}
					 }
					 """;

		return _reader.ReadFromJson(json, versionDirectory: null, PluginId, "1.0.0");
	}

	private void AssertRejected(string? extraBlocks, PluginManifestError expected, string? entrypoints = null)
	{
		var result = Read(extraBlocks, entrypoints);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(expected));
		});
	}

	[Test]
	public void A_manifest_using_none_of_the_new_blocks_is_valid()
	{
		var result = Read(null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Publisher, Is.Null);
			Assert.That(result.Manifest.Compatibility, Is.Null);
			Assert.That(result.Manifest.Permissions, Is.Null);
			Assert.That(result.Manifest.Signature, Is.Null);
		});
	}

	[Test]
	public void A_manifest_using_every_new_block_round_trips()
	{
		var result = Read($$"""
							"publisher": { "name": "Suchbyte", "id": "com.suchbyte", "url": "https://macro-deck.app" },
							"license": "MIT",
							"homepage": "https://macro-deck.app",
							"compatibility": {
								"sdk": ">=1.0.0,<2.0.0",
								"protocol": { "minimum": 1, "maximum": 1 },
								"macroDeck": ">=3.0.0"
							},
							"permissions": [ "host:variables" ],
							"dependencies": [ { "id": "com.other.dep", "versionRange": ">=1.0.0", "optional": true } ],
							"conflicts": [ { "id": "com.other.rival" } ],
							"iconPacks": [ { "id": "com.suchbyte.icons", "optional": false } ],
							"files": [
								{ "path": "TestPlugin", "sha256": "sha256:{{new string('a', 64)}}", "size": 6 }
							],
							"signature": {
								"algorithm": "ed25519",
								"keyId": "macro-deck-root",
								"value": "{{Convert.ToBase64String(new byte[64])}}"
							}
							""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Publisher!.Name, Is.EqualTo("Suchbyte"));
			Assert.That(result.Manifest.Compatibility!.Protocol!.Maximum, Is.EqualTo(1));
			Assert.That(result.Manifest.Dependencies, Has.Count.EqualTo(1));
			Assert.That(result.Manifest.Dependencies![0].Optional, Is.True);
			Assert.That(result.Manifest.IconPacks![0].Optional, Is.False);
			Assert.That(result.Manifest.Files, Has.Count.EqualTo(1));
			Assert.That(result.Manifest.Signature!.KeyId, Is.EqualTo("macro-deck-root"));
		});
	}

	// --- entrypoint runtime -------------------------------------------------------------------

	[Test]
	public void An_absent_runtime_block_means_self_contained()
	{
		var result = Read(null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Entrypoints["linux-x64"].Runtime, Is.Null);
		});
	}

	[Test]
	public void A_framework_dependent_entrypoint_declaring_a_dotnet_version_is_valid()
	{
		var result = Read(null,
			"""
			"entrypoints": {
				"linux-x64": {
					"executable": "TestPlugin.dll",
					"runtime": { "kind": "FrameworkDependent", "dotnetVersion": "10.0" }
				}
			}
			""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Entrypoints["linux-x64"].Runtime!.Kind,
				Is.EqualTo(PluginEntrypointRuntimeKind.FrameworkDependent));
		});
	}

	[TestCase("""{ "kind": "FrameworkDependent" }""")]
	[TestCase("""{ "kind": "FrameworkDependent", "dotnetVersion": "ten" }""")]
	[TestCase("""{ "kind": "FrameworkDependent", "dotnetVersion": "10" }""")]
	public void A_framework_dependent_entrypoint_without_a_usable_dotnet_version_is_rejected(string runtime)
	{
		AssertRejected(null,
			PluginManifestError.InvalidEntrypointRuntime,
			$$"""
			  "entrypoints": { "linux-x64": { "executable": "TestPlugin.dll", "runtime": {{runtime}} } }
			  """);
	}

	/// <summary>A managed assembly launched directly rather than through the muxer fails with an opaque
	/// operating system error, so it is refused where the reason is still visible.</summary>
	[Test]
	public void A_self_contained_entrypoint_naming_a_dll_is_rejected()
	{
		AssertRejected(null,
			PluginManifestError.InvalidEntrypointRuntime,
			"""
			"entrypoints": { "linux-x64": { "executable": "TestPlugin.dll" } }
			""");
	}

	/// <summary>The artifact format runs nothing of its own during installation, so a script standing in
	/// as the entrypoint would be an install hook by another name.</summary>
	[TestCase("start.sh")]
	[TestCase("start.bat")]
	[TestCase("start.cmd")]
	[TestCase("start.ps1")]
	public void A_script_entrypoint_is_rejected(string executable)
	{
		AssertRejected(null,
			PluginManifestError.InvalidEntrypointRuntime,
			$$"""
			  "entrypoints": { "linux-x64": { "executable": "{{executable}}" } }
			  """);
	}

	// --- dependencies and conflicts -----------------------------------------------------------

	[Test]
	public void A_dependency_naming_an_invalid_plugin_id_is_rejected()
	{
		AssertRejected("""
					   "dependencies": [ { "id": "Not A Plugin Id" } ]
					   """,
			PluginManifestError.InvalidDependency);
	}

	[Test]
	public void A_dependency_with_an_unparseable_version_range_is_rejected()
	{
		AssertRejected("""
					   "dependencies": [ { "id": "com.other.dep", "versionRange": "^1.0.0" } ]
					   """,
			PluginManifestError.InvalidDependency);
	}

	[Test]
	public void A_plugin_declaring_a_dependency_on_itself_is_rejected()
	{
		AssertRejected($$"""
						 "dependencies": [ { "id": "{{PluginId}}" } ]
						 """,
			PluginManifestError.InvalidDependency);
	}

	[Test]
	public void The_same_dependency_declared_twice_is_rejected()
	{
		AssertRejected("""
					   "dependencies": [ { "id": "com.other.dep" }, { "id": "com.other.dep" } ]
					   """,
			PluginManifestError.InvalidDependency);
	}

	/// <summary>Declaring the same plugin as both required and forbidden can never be satisfied, so it
	/// is an authoring mistake rather than a runtime condition.</summary>
	[Test]
	public void An_id_declared_as_both_a_dependency_and_a_conflict_is_rejected()
	{
		AssertRejected("""
					   "dependencies": [ { "id": "com.other.dep" } ],
					   "conflicts": [ { "id": "com.other.dep" } ]
					   """,
			PluginManifestError.InvalidDependency);
	}

	// --- permissions --------------------------------------------------------------------------

	/// <summary>An unknown permission survives validation on purpose: a manifest written for a newer host
	/// must still install, and the installer reports the unknown entry as advisory.</summary>
	[Test]
	public void An_unknown_permission_string_is_accepted()
	{
		var result = Read("""
						  "permissions": [ "totally.made.up" ]
						  """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Permissions, Is.EqualTo(_singleUnknownPermission));
		});
	}

	[Test]
	public void A_blank_or_duplicated_permission_is_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Read("""
							 "permissions": [ "  " ]
							 """).Error,
				Is.EqualTo(PluginManifestError.InvalidPermission));

			Assert.That(Read("""
							 "permissions": [ "host:deck", "host:deck" ]
							 """).Error,
				Is.EqualTo(PluginManifestError.InvalidPermission));
		});
	}

	// --- languages ----------------------------------------------------------------------------

	/// <summary>A language tag nothing in this build recognises survives validation for the same reason an
	/// unknown permission does: it is a declaration a store reads, and a manifest naming a language this
	/// host has never heard of must still install and run.</summary>
	[Test]
	public void An_unrecognised_language_tag_is_accepted()
	{
		var result = Read("""
						  "languages": [ "en", "zh-Hant-TW", "qya" ]
						  """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Languages, Is.EqualTo(_threeLanguages));
		});
	}

	/// <summary>Case-insensitively duplicated, unlike permissions: a BCP-47 tag carries no meaning in its
	/// case, so 'de' and 'DE' name one language twice.</summary>
	[Test]
	public void A_blank_or_duplicated_language_is_rejected()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Read("""
							 "languages": [ "  " ]
							 """).Error,
				Is.EqualTo(PluginManifestError.InvalidLanguage));

			Assert.That(Read("""
							 "languages": [ "de", "DE" ]
							 """).Error,
				Is.EqualTo(PluginManifestError.InvalidLanguage));
		});
	}

	// --- file digests -------------------------------------------------------------------------

	[TestCase("\"path\": \"../escape\", \"sha256\": \"sha256:PLACEHOLDER\", \"size\": 1")]
	[TestCase("\"path\": \"/absolute\", \"sha256\": \"sha256:PLACEHOLDER\", \"size\": 1")]
	[TestCase("\"path\": \"ok\", \"sha256\": \"md5:PLACEHOLDER\", \"size\": 1")]
	[TestCase("\"path\": \"ok\", \"sha256\": \"sha256:tooshort\", \"size\": 1")]
	[TestCase("\"path\": \"ok\", \"sha256\": \"sha256:PLACEHOLDER\", \"size\": -1")]
	public void A_malformed_file_digest_is_rejected(string entry)
	{
		AssertRejected($$"""
						 "files": [ { {{entry.Replace("PLACEHOLDER", new string('a', 64), StringComparison.Ordinal)}} } ]
						 """,
			PluginManifestError.InvalidFileDigest);
	}

	/// <summary>An upper-case digest would compare unequal against the lower-case hex the host computes,
	/// so the one accepted spelling is pinned here rather than normalised later.</summary>
	[Test]
	public void An_upper_case_file_digest_is_rejected()
	{
		AssertRejected($$"""
						 "files": [ { "path": "ok", "sha256": "sha256:{{new string('A', 64)}}", "size": 1 } ]
						 """,
			PluginManifestError.InvalidFileDigest);
	}

	[Test]
	public void The_same_file_declared_twice_is_rejected()
	{
		AssertRejected($$"""
						 "files": [
						 	{ "path": "ok", "sha256": "sha256:{{new string('a', 64)}}", "size": 1 },
						 	{ "path": "ok", "sha256": "sha256:{{new string('b', 64)}}", "size": 2 }
						 ]
						 """,
			PluginManifestError.InvalidFileDigest);
	}

	// --- compatibility and signature ------------------------------------------------------------

	[Test]
	public void An_unparseable_compatibility_range_is_rejected()
	{
		AssertRejected("""
					   "compatibility": { "macroDeck": "~3.0" }
					   """,
			PluginManifestError.InvalidCompatibility);
	}

	[Test]
	public void An_inverted_protocol_range_is_rejected()
	{
		AssertRejected("""
					   "compatibility": { "protocol": { "minimum": 5, "maximum": 2 } }
					   """,
			PluginManifestError.InvalidCompatibility);
	}

	/// <summary>A range this host does not satisfy is a perfectly well-formed manifest. Whether to refuse
	/// it is the installer's call, made against the running version, not the reader's.</summary>
	[Test]
	public void A_compatibility_range_this_host_does_not_satisfy_still_reads_successfully()
	{
		var result = Read("""
						  "compatibility": { "protocol": { "minimum": 99, "maximum": 100 } }
						  """);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[TestCase("\"algorithm\": \"\", \"keyId\": \"k\", \"value\": \"QQ==\"")]
	[TestCase("\"algorithm\": \"ed25519\", \"keyId\": \" \", \"value\": \"QQ==\"")]
	[TestCase("\"algorithm\": \"ed25519\", \"keyId\": \"k\", \"value\": \"not base64!\"")]
	public void A_malformed_signature_block_is_rejected(string signature)
	{
		AssertRejected($$"""
						 "signature": { {{signature}} }
						 """,
			PluginManifestError.InvalidSignature);
	}

	/// <summary>
	/// The reader deliberately does not judge the algorithm or the signature length. A host that refused
	/// a scheme it had not heard of could never be given a new one without a manifest format change.
	/// </summary>
	[Test]
	public void A_signature_naming_an_unknown_algorithm_still_reads_successfully()
	{
		var result = Read("""
						  "signature": { "algorithm": "some-future-scheme", "keyId": "k", "value": "QQ==" }
						  """);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	// --- publisher ----------------------------------------------------------------------------

	[Test]
	public void A_publisher_without_a_name_is_rejected()
	{
		AssertRejected("""
					   "publisher": { "name": " " }
					   """,
			PluginManifestError.InvalidPublisher);
	}

	[Test]
	public void A_publisher_id_that_is_not_reverse_domain_is_rejected()
	{
		AssertRejected("""
					   "publisher": { "name": "Suchbyte", "id": "Suchbyte" }
					   """,
			PluginManifestError.InvalidPublisher);
	}

	// --- unchanged behaviour ------------------------------------------------------------------

	/// <summary>
	/// A cross-issue guard: #412 clamps these rather than refusing them, and a new validator that started
	/// rejecting out-of-range values would break manifests that already install today.
	/// </summary>
	[Test]
	public void Out_of_range_supervisor_settings_are_still_clamped_rather_than_rejected()
	{
		var result = Read("""
						  "shutdown": { "gracefulTimeoutSeconds": 999 },
						  "health": { "intervalSeconds": 1, "timeoutSeconds": 99, "unhealthyThreshold": 99 }
						  """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Manifest!.Shutdown!.GracefulTimeoutSeconds, Is.EqualTo(60));
			Assert.That(result.Manifest.Health!.IntervalSeconds, Is.EqualTo(5));
			Assert.That(result.Manifest.Health.TimeoutSeconds, Is.EqualTo(10));
			Assert.That(result.Manifest.Health.UnhealthyThreshold, Is.EqualTo(10));
		});
	}

	/// <summary>An escaping entrypoint path is invalid on every platform, so it must be caught even when
	/// there is no extracted directory yet to resolve against.</summary>
	[TestCase("../escape")]
	[TestCase("/absolute/path")]
	public void An_entrypoint_path_leaving_the_version_directory_is_rejected_without_a_directory(string path)
	{
		var result = Read(null,
			$$"""
			  "entrypoints": { "linux-x64": { "executable": "{{path}}" } }
			  """);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error,
				Is.EqualTo(PluginManifestError.EntrypointOutsideVersionDirectory));
		});
	}
}
