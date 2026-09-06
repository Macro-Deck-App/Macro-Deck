using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Manifest;

[TestFixture]
internal sealed class PluginManifestReaderTests
{
	private string _root = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-manifest-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}

	private string CreateVersionDirectory(string pluginId = "com.example.plugin", string version = "1.0.0")
	{
		var versionDirectory = Path.Combine(_root, pluginId, "versions", version);
		Directory.CreateDirectory(versionDirectory);
		return versionDirectory;
	}

	private static string CurrentRid => PluginRuntimeIdentifiers.Current;

	private static void WriteExecutable(string versionDirectory, string relativePath)
	{
		var full = Path.Combine(versionDirectory, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, "#!/bin/sh\n");
	}

	private static string ManifestJson(string id, string version, string rid, string executable, string extra = "")
		=> $$"""
			 {
			 	"manifestVersion": 1,
			 	"id": "{{id}}",
			 	"name": "Example",
			 	"version": "{{version}}",
			 	"entrypoints": {
			 		"{{rid}}": { "executable": "{{executable}}" }
			 	}
			 	{{extra}}
			 }
			 """;

	[Test]
	public void A_manifest_over_the_artifact_limit_is_rejected_before_it_is_parsed()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "plugin");

		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		var padding = new string(' ', PluginArtifactLimits.MaxManifestBytes);
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "plugin") + padding);

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.Malformed));
		});
	}

	[Test]
	public void A_valid_manifest_round_trips()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "app"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.Multiple(() =>
		{
			Assert.That(result.Manifest!.Id, Is.EqualTo("com.example.plugin"));
			Assert.That(result.Manifest.Version, Is.EqualTo("1.0.0"));
			Assert.That(result.Manifest.Entrypoints, Contains.Key(CurrentRid));
		});
	}

	[Test]
	public void An_unknown_top_level_property_is_ignored()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "app", """, "somethingFromTheFuture": 123"""));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public void An_unsupported_manifest_version_is_rejected_before_full_deserialization()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, """{ "manifestVersion": 2, "id": "x" }""");

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.UnsupportedManifestVersion));
		});
	}

	[Test]
	public void An_id_mismatch_with_the_owning_directory_is_rejected()
	{
		var versionDirectory = CreateVersionDirectory("com.example.plugin", "1.0.0");
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, ManifestJson("com.other.plugin", "1.0.0", CurrentRid, "app"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.IdMismatch));
		});
	}

	[Test]
	public void A_version_mismatch_with_the_owning_directory_is_rejected()
	{
		var versionDirectory = CreateVersionDirectory("com.example.plugin", "1.0.0");
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, ManifestJson("com.example.plugin", "2.0.0", CurrentRid, "app"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.VersionMismatch));
		});
	}

	[Test]
	public void A_relative_traversal_executable_path_is_rejected()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "../../etc/passwd"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.EntrypointOutsideVersionDirectory));
		});
	}

	[Test]
	public void An_absolute_executable_path_is_rejected()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		var absolute = OperatingSystem.IsWindows() ? "C:\\\\Windows\\\\evil.exe" : "/usr/bin/evil";
		File.WriteAllText(manifestPath, ManifestJson("com.example.plugin", "1.0.0", CurrentRid, absolute));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.EntrypointOutsideVersionDirectory));
		});
	}

	[Test]
	public void Empty_entrypoints_are_rejected()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			"""
			{
				"manifestVersion": 1,
				"id": "com.example.plugin",
				"name": "Example",
				"version": "1.0.0",
				"entrypoints": {}
			}
			""");

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.NoEntrypoints));
		});
	}

	[Test]
	public void UnhealthyThreshold_of_one_is_clamped_to_two()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				""", "health": { "unhealthyThreshold": 1 }"""));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Health!.UnhealthyThreshold, Is.EqualTo(2));
	}

	[Test]
	public void GracefulTimeoutSeconds_above_the_ceiling_is_clamped()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				""", "shutdown": { "gracefulTimeoutSeconds": 9999 }"""));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Shutdown!.GracefulTimeoutSeconds, Is.EqualTo(60));
	}

	[Test]
	public void A_missing_manifest_file_reports_NotFound()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.NotFound));
		});
	}

	[Test]
	public void Truncated_json_reports_Malformed()
	{
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, """{ "manifestVersion": 1, "id": "com.example.plugin""");

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.Malformed));
		});
	}

	[Test]
	public void A_manifest_missing_the_current_RID_entrypoint_still_reads_successfully()
	{
		// No entrypoint for this host is a supervisor-time NoEntrypointForRuntime decision, not a
		// manifest read failure - the manifest may still be perfectly valid for another platform.
		var versionDirectory = CreateVersionDirectory();
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin", "1.0.0", "some-other-rid", "app"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public void A_manifest_with_no_icon_reads_successfully_and_declares_none()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "app"));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Icon, Is.Null);
	}

	[Test]
	public void A_declared_icon_is_carried_through_even_though_no_such_file_exists()
	{
		// The single most important host-side icon test: the reader must never check that a declared
		// icon exists on disk. It gates every supervisor launch, so an existence check here would let a
		// stripped or quarantined icon file stop an otherwise-working plugin from running.
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				IconExtra("assets/icon.png")));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Icon, Is.EqualTo("assets/icon.png"));
	}

	[TestCase("../icon.png")]
	[TestCase("assets/../../icon.png")]
	[TestCase("assets/../icon.png")]
	[TestCase("..")]
	public void An_icon_path_that_escapes_the_version_directory_is_rejected(string icon)
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				IconExtra(icon)));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidIcon));
		});
	}

	[Test]
	public void A_rooted_icon_path_is_rejected()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		var rooted = OperatingSystem.IsWindows() ? "C:\\icon.png" : "/etc/icon.png";
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				IconExtra(rooted)));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidIcon));
		});
	}

	[TestCase("")]
	[TestCase("   ")]
	public void A_blank_icon_path_is_rejected(string icon)
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				IconExtra(icon)));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidIcon));
		});
	}

	[Test]
	public void An_invalid_icon_is_rejected_when_there_is_no_version_directory_yet()
	{
		// The installer validates an artifact before any version directory exists. Icon validation must
		// not hang off the version-directory branch, or a traversing path would install and only fail
		// later.
		var json = ManifestJson("com.example.plugin", "1.0.0", CurrentRid, "app", IconExtra("../icon.png"));

		var result = new PluginManifestReader()
			.ReadFromJson(json, versionDirectory: null, "com.example.plugin", "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidIcon));
		});
	}

	[Test]
	public void A_well_formed_icon_pointing_at_a_nested_path_reads_successfully()
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath,
			ManifestJson("com.example.plugin",
				"1.0.0",
				CurrentRid,
				"app",
				IconExtra("assets/icons/plugin-icon@2x.png")));

		var result = new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Icon, Is.EqualTo("assets/icons/plugin-icon@2x.png"));
	}

	/// <summary>Builds an <c>extra</c> fragment for <see cref="ManifestJson"/> that declares
	/// <paramref name="icon"/>, escaping it for embedding in a JSON string literal.</summary>
	private static string IconExtra(string icon)
		=> $", \"icon\": {JsonSerializer.Serialize(icon)}";

	/// <summary>Like <see cref="ManifestJson"/> but with a caller-supplied <c>name</c>, JSON-escaped so a
	/// name containing a literal control character or quote can still be embedded safely.</summary>
	private static string ManifestJsonWithName(string name,
		string id = "com.example.plugin",
		string version = "1.0.0")
		=> $$"""
			 {
			 	"manifestVersion": 1,
			 	"id": "{{id}}",
			 	"name": {{JsonSerializer.Serialize(name)}},
			 	"version": "{{version}}",
			 	"entrypoints": {
			 		"{{CurrentRid}}": { "executable": "app" }
			 	}
			 }
			 """;

	private PluginManifestReadResult ReadWithName(string name)
	{
		var versionDirectory = CreateVersionDirectory();
		WriteExecutable(versionDirectory, "app");
		var manifestPath = Path.Combine(versionDirectory, "manifest.json");
		File.WriteAllText(manifestPath, ManifestJsonWithName(name));

		return new PluginManifestReader().Read(manifestPath, "com.example.plugin", "1.0.0");
	}

	[TestCase("   ")]
	[TestCase("")]
	public void A_blank_manifest_name_is_rejected(string name)
	{
		var result = ReadWithName(name);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidName));
			Assert.That(result.Error, Is.Not.EqualTo(PluginManifestError.InvalidPluginId));
			Assert.That(result.Error, Is.Not.EqualTo(PluginManifestError.InvalidPublisher));
			Assert.That(result.Error, Is.Not.EqualTo(PluginManifestError.Malformed));
		});
	}

	/// <summary>An embedded control character is rejected, but neither an ordinary letters-only name nor
	/// one containing a plain space is - a validator that flagged either of those would be over-eager.</summary>
	[Test]
	public void A_manifest_name_containing_control_characters_is_rejected()
	{
		Assert.That(ReadWithName("WeatherWidget").Success, Is.True);
		Assert.That(ReadWithName("Weather Widget").Success, Is.True);

		var result = ReadWithName("Weather\nWidget");
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginManifestError.InvalidName));
		});
	}

	[Test]
	public void The_manifest_name_length_bound_is_inclusive_at_128()
	{
		var atLimit = ReadWithName(new string('a', 128));
		var overLimit = ReadWithName(new string('a', 129));

		Assert.Multiple(() =>
		{
			Assert.That(atLimit.Success, Is.True, atLimit.ErrorMessage);
			Assert.That(overLimit.Success, Is.False);
			Assert.That(overLimit.Error, Is.EqualTo(PluginManifestError.InvalidName));
		});
	}

	[TestCase("Weather Widget")]
	[TestCase("Bob's Plugin (v2) - beta")]
	[TestCase("Wetterübersicht")]
	[TestCase("天気ウィジェット")]
	public void Ordinary_manifest_names_keep_loading(string name)
	{
		var result = ReadWithName(name);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
		Assert.That(result.Manifest!.Name, Is.EqualTo(name));
	}
}
