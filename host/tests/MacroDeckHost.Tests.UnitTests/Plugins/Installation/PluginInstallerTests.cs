using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallerTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private static readonly string[] _publicationVocabulary =
		["publisher", "description", "icon", "license", "repository", "compatibility", "publication"];

	private TestPaths _paths = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private PluginInstallationCatalog _catalog = null!;
	private PluginInstaller _installer = null!;
	private FakeIntegrationRegistrar _integrationRegistrar = null!;
	private string _sourceDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.PluginStagingDirectory);
		Directory.CreateDirectory(_paths.PluginCacheDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "artifacts");
		Directory.CreateDirectory(_sourceDirectory);

		_catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_integrationRegistrar = new FakeIntegrationRegistrar();

		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with
		{
			ActivationHealthTimeout = TimeSpan.FromSeconds(2)
		};

		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(_catalog, sessionRegistry);
		var logRateLimiter = new PluginLogRateLimiter(TimeProvider.System);
		var forgetter = new PluginIdentityForgetter(
			new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance),
			logRateLimiter,
			new PluginLogIngestor(logRateLimiter, sessionRegistry, new FakePluginSupervisor(), TimeProvider.System));

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository, InMemoryPluginRegistrationRepository>();
		services.AddSingleton<IPluginAccessTokenRepository, InMemoryPluginAccessTokenRepository>();
		services.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		services.AddSingleton<IPluginRegistrationService>(provider => new PluginRegistrationService(
			provider.GetRequiredService<IPluginRegistrationRepository>(),
			provider.GetRequiredService<IPluginAccessTokenRepository>(),
			sessionRegistry,
			forgetter,
			_catalog,
			TimeProvider.System));
		var provider = services.BuildServiceProvider();

		_installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(),
				options,
				Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator(),
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			_integrationRegistrar,
			sessionRegistry,
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private string BuildArtifact(string version = "1.0.0",
		string? extraManifestBlocks = null,
		string pluginId = PluginId,
		string payload = "binary",
		string fileName = "plugin.macroDeckPlugin")
	{
		return new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, extraManifestBlocks))
			.WithFile(ManifestJson.EntrypointExecutable, payload)
			.WriteTo(_sourceDirectory, fileName);
	}

	private Task<PluginInstallResult> Install(string artifactPath, PluginInstallRequest? request = null)
	{
		return _installer.Install(PluginArtifactSource.FromPath(artifactPath),
			request ?? new PluginInstallRequest());
	}

	private string? ActiveVersion()
	{
		var path = PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, PluginId);
		if (!File.Exists(path))
		{
			return null;
		}

		using var document = JsonDocument.Parse(File.ReadAllText(path));
		return document.RootElement.GetProperty("version").GetString();
	}

	private string VersionDirectory(string version)
		=> PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, PluginId, version);

	private string DataDirectory() => PluginInstallPaths.DataDirectory(_paths.PluginsDirectory, PluginId);

	private void WritePluginData(string content)
	{
		Directory.CreateDirectory(DataDirectory());
		File.WriteAllText(Path.Combine(DataDirectory(), "settings.json"), content);
	}


	[Test]
	public async Task A_fresh_install_activates_the_version_and_writes_the_documented_layout()
	{
		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.PluginId, Is.EqualTo(PluginId));
			Assert.That(result.Version, Is.EqualTo("1.0.0"));
			Assert.That(result.PreviousVersion, Is.Null);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.RolledBack, Is.False);
			Assert.That(result.Error, Is.Null);
			Assert.That(File.Exists(Path.Combine(VersionDirectory("1.0.0"), "manifest.json")), Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task An_installed_plugin_is_discoverable_by_the_installation_catalog()
	{
		await Install(BuildArtifact());

		var installed = _catalog.Discover().SingleOrDefault(plugin => plugin.PluginId == PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(installed, Is.Not.Null);
			Assert.That(installed!.ActiveVersion?.Version, Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task Staging_and_cache_directories_are_invisible_to_the_installation_catalog()
	{
		await Install(BuildArtifact());

		var ids = _catalog.Discover().Select(plugin => plugin.PluginId).ToList();

		Assert.That(ids, Is.EqualTo(new[] { PluginId }));
	}

	[Test]
	public async Task Staging_is_emptied_after_a_successful_install()
	{
		await Install(BuildArtifact());

		Assert.That(Directory.EnumerateDirectories(_paths.PluginStagingDirectory), Is.Empty);
	}

	[Test]
	public async Task An_update_records_the_previous_version_and_keeps_it_installed()
	{
		await Install(BuildArtifact());
		var result = await Install(BuildArtifact("1.1.0", fileName: "plugin-110.macroDeckPlugin"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Version, Is.EqualTo("1.1.0"));
			Assert.That(result.PreviousVersion, Is.EqualTo("1.0.0"));
			Assert.That(ActiveVersion(), Is.EqualTo("1.1.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")), Is.True);
		});
	}

	[Test]
	public async Task Reinstalling_an_installed_version_without_force_changes_nothing()
	{
		await Install(BuildArtifact(payload: "original"));

		var result = await Install(BuildArtifact(payload: "replacement",
			fileName: "again.macroDeckPlugin"));

		var installedPayload = File.ReadAllText(
			Path.Combine(VersionDirectory("1.0.0"), ManifestJson.EntrypointExecutable));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.AlreadyInstalled));
			Assert.That(installedPayload, Is.EqualTo("original"));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task A_forced_reinstall_replaces_the_version_contents()
	{
		await Install(BuildArtifact(payload: "original"));
		File.WriteAllText(Path.Combine(VersionDirectory("1.0.0"), "stale.txt"), "left over");

		var result = await Install(BuildArtifact(payload: "replacement", fileName: "again.macroDeckPlugin"),
			new PluginInstallRequest { Force = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(File.ReadAllText(Path.Combine(VersionDirectory("1.0.0"), ManifestJson.EntrypointExecutable)),
				Is.EqualTo("replacement"));
			Assert.That(File.Exists(Path.Combine(VersionDirectory("1.0.0"), "stale.txt")), Is.False);
		});
	}

	[Test]
	public async Task Inspecting_an_artifact_writes_nothing_outside_staging()
	{
		var result = await _installer.Inspect(PluginArtifactSource.FromPath(BuildArtifact()));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.PluginId, Is.EqualTo(PluginId));
			Assert.That(result.Activated, Is.False);
			Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
			Assert.That(Directory.EnumerateDirectories(_paths.PluginStagingDirectory), Is.Empty);
		});
	}


	[Test]
	public async Task An_artifact_whose_manifest_sits_in_a_wrapper_directory_is_rejected()
	{
		var artifact = new PluginArtifactBuilder()
			.WithFile($"{PluginId}/manifest.json", ManifestJson.Minimal())
			.WithFile($"{PluginId}/{ManifestJson.EntrypointExecutable}", "binary")
			.WriteTo(_sourceDirectory, "wrapped.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.ManifestMissing));
		});
	}

	[Test]
	public async Task An_artifact_containing_a_traversal_entry_is_rejected_and_writes_nothing_outside()
	{
		await Install(BuildArtifact());

		var hostile = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("2.0.0", PluginId, null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WithFile("../../escaped.txt", "owned")
			.WriteTo(_sourceDirectory, "hostile.macroDeckPlugin");

		var result = await Install(hostile);
		var escapedPath = Path.Combine(_paths.PluginsDirectory, "escaped.txt");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.UnsafeEntry));
			Assert.That(File.Exists(escapedPath), Is.False);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.False);
			Assert.That(Directory.EnumerateDirectories(_paths.PluginStagingDirectory), Is.Empty);
		});
	}

	[Test]
	public async Task An_artifact_containing_a_symlink_entry_is_rejected()
	{
		const int symlinkMode = 0xA1FF;

		var hostile = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Minimal())
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WithRawEntry("link", "../../", symlinkMode)
			.WriteTo(_sourceDirectory, "symlink.macroDeckPlugin");

		var result = await Install(hostile);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.UnsafeEntry));
			Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
		});
	}

	[Test]
	public async Task An_artifact_naming_a_different_plugin_than_requested_is_rejected()
	{
		var result = await Install(BuildArtifact(),
			new PluginInstallRequest { ExpectedPluginId = "com.suchbyte.other" });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.IdMismatch));
		});
	}

	[Test]
	public async Task An_artifact_requiring_a_protocol_this_host_does_not_speak_is_rejected()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"compatibility": { "protocol": { "minimum": 99, "maximum": 100 } }
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.Incompatible));
			Assert.That(ActiveVersion(), Is.Null);
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")), Is.False);
		});
	}

	[Test]
	public async Task A_declared_file_digest_that_does_not_match_is_rejected()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			$$"""
			  "files": [
			  	{
			  		"path": "{{ManifestJson.EntrypointExecutable}}",
			  		"sha256": "sha256:{{new string('a', 64)}}",
			  		"size": 6
			  	}
			  ]
			  """);

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HashMismatch));
			Assert.That(ActiveVersion(), Is.Null);
		});
	}

	[Test]
	public async Task A_file_present_in_the_artifact_but_absent_from_the_declared_list_is_rejected()
	{
		var payload = Encoding.UTF8.GetBytes("binary");
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				$$"""
				  "files": [
				  	{
				  		"path": "{{ManifestJson.EntrypointExecutable}}",
				  		"sha256": "{{PluginArtifactBuilder.Sha256Of(payload)}}",
				  		"size": {{payload.Length}}
				  	}
				  ]
				  """))
			.WithFile(ManifestJson.EntrypointExecutable, payload)
			.WithFile("undeclared.dll", "extra")
			.WriteTo(_sourceDirectory, "undeclared.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HashMismatch));
		});
	}

	[Test]
	public async Task An_artifact_whose_declared_digests_all_match_installs()
	{
		var payload = Encoding.UTF8.GetBytes("binary");
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				$$"""
				  "files": [
				  	{
				  		"path": "{{ManifestJson.EntrypointExecutable}}",
				  		"sha256": "{{PluginArtifactBuilder.Sha256Of(payload)}}",
				  		"size": {{payload.Length}}
				  	}
				  ]
				  """))
			.WithFile(ManifestJson.EntrypointExecutable, payload)
			.WriteTo(_sourceDirectory, "digested.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public async Task A_signed_artifact_carrying_certificate_material_installs()
	{
		var payload = Encoding.UTF8.GetBytes("binary");
		var signature = Convert.ToBase64String(new byte[64]);
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				$$"""
				  "files": [
				  	{
				  		"path": "{{ManifestJson.EntrypointExecutable}}",
				  		"sha256": "{{PluginArtifactBuilder.Sha256Of(payload)}}",
				  		"size": {{payload.Length}}
				  	}
				  ],
				  "signature": { "algorithm": "ed25519", "keyId": "unknown-key", "value": "{{signature}}" }
				  """))
			.WithFile(ManifestJson.EntrypointExecutable, payload)
			.WithFile("certificate.json", "{}")
			.WithFile("certificate.sig", "sig")
			.WriteTo(_sourceDirectory, "signed-with-certificate.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public async Task An_unsigned_artifact_carrying_certificate_material_is_rejected_as_undeclared()
	{
		var payload = Encoding.UTF8.GetBytes("binary");
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				$$"""
				  "files": [
				  	{
				  		"path": "{{ManifestJson.EntrypointExecutable}}",
				  		"sha256": "{{PluginArtifactBuilder.Sha256Of(payload)}}",
				  		"size": {{payload.Length}}
				  	}
				  ]
				  """))
			.WithFile(ManifestJson.EntrypointExecutable, payload)
			.WithFile("certificate.json", "{}")
			.WithFile("certificate.sig", "sig")
			.WriteTo(_sourceDirectory, "unsigned-with-certificate.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HashMismatch));
		});
	}

	[Test]
	public async Task A_local_artifact_is_never_deleted_by_the_installer()
	{
		var artifact = BuildArtifact();

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(File.Exists(artifact), Is.True);
		});
	}


	[Test]
	public async Task An_unsigned_artifact_installs()
	{
		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.HasBlockingWarning, Is.False);
		});
	}

	[Test]
	public async Task A_well_formed_signature_this_host_cannot_verify_does_not_block_installation()
	{
		var signature = Convert.ToBase64String(new byte[64]);
		var artifact = BuildArtifact(extraManifestBlocks:
			$$"""
			  "signature": { "algorithm": "ed25519", "keyId": "unknown-key", "value": "{{signature}}" }
			  """);

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.HasBlockingWarning, Is.False);
		});
	}

	[Test]
	public async Task A_signature_of_an_unknown_algorithm_does_not_block_installation()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			$$"""
			  "signature": {
			  	"algorithm": "some-future-scheme",
			  	"keyId": "k",
			  	"value": "{{Convert.ToBase64String(new byte[64])}}"
			  }
			  """);

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.HasBlockingWarning, Is.False);
		});
	}

	[Test]
	public async Task A_malformed_signature_value_is_rejected()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"signature": { "algorithm": "ed25519", "keyId": "k", "value": "not base64!" }
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.ManifestInvalid));
		});
	}

	[Test]
	public async Task An_update_that_never_becomes_healthy_rolls_back_to_the_previous_version()
	{
		await Install(BuildArtifact());
		_supervisor.UnhealthyPlugins.Add(PluginId);

		var result = await Install(BuildArtifact("1.1.0", fileName: "plugin-110.macroDeckPlugin"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HealthValidationFailed));
			Assert.That(result.RolledBack, Is.True);
			Assert.That(result.PreviousVersion, Is.EqualTo("1.0.0"));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")), Is.True);
			Assert.That(Directory.Exists(VersionDirectory("1.1.0")), Is.False);
		});
	}

	[Test]
	public async Task A_first_install_that_never_becomes_healthy_leaves_the_plugin_not_installed()
	{
		_supervisor.UnhealthyPlugins.Add(PluginId);

		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.HealthValidationFailed));
			Assert.That(result.PreviousVersion, Is.Null);
			Assert.That(ActiveVersion(), Is.Null);
			Assert.That(_catalog.Discover().Any(plugin => plugin.ActiveVersion is not null), Is.False);
		});
	}

	[Test]
	public async Task A_forced_reinstall_of_the_active_version_that_fails_health_restores_the_original()
	{
		await Install(BuildArtifact(payload: "original"));
		_supervisor.UnhealthyPlugins.Add(PluginId);

		var result = await Install(BuildArtifact(payload: "broken", fileName: "broken.macroDeckPlugin"),
			new PluginInstallRequest { Force = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.RolledBack, Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")),
				Is.True,
				"the version current.json names must still exist after a rollback");
			Assert.That(File.ReadAllText(Path.Combine(VersionDirectory("1.0.0"), ManifestJson.EntrypointExecutable)),
				Is.EqualTo("original"));
		});
	}

	[Test]
	public async Task Activating_a_version_that_fails_health_leaves_that_version_installed()
	{
		await Install(BuildArtifact());
		await Install(BuildArtifact("2.0.0", fileName: "plugin-200.macroDeckPlugin"));
		_supervisor.UnhealthyPlugins.Add(PluginId);

		var result = await _installer.Activate(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.RolledBack, Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("2.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")), Is.True);
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.True);
		});
	}

	[Test]
	public async Task Current_json_never_names_a_version_that_is_not_on_disk()
	{
		await Install(BuildArtifact());

		var hostile = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("2.0.0", PluginId, null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WithFile("../../escaped.txt", "owned")
			.WriteTo(_sourceDirectory, "hostile.macroDeckPlugin");

		await Install(hostile);
		await Install(BuildArtifact("3.0.0",
			extraManifestBlocks:
			"""
			"compatibility": { "protocol": { "minimum": 99, "maximum": 100 } }
			""",
			fileName: "incompatible.macroDeckPlugin"));

		_supervisor.UnhealthyPlugins.Add(PluginId);
		await Install(BuildArtifact("4.0.0", fileName: "plugin-400.macroDeckPlugin"));

		var active = ActiveVersion();

		Assert.Multiple(() =>
		{
			Assert.That(active, Is.Not.Null);
			Assert.That(File.Exists(Path.Combine(VersionDirectory(active!), "manifest.json")), Is.True);
		});
	}

	[Test]
	public async Task Plugin_data_survives_an_update_at_a_path_outside_any_version_directory()
	{
		await Install(BuildArtifact());
		WritePluginData("keep me");

		await Install(BuildArtifact("1.1.0", fileName: "plugin-110.macroDeckPlugin"));

		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(Path.Combine(DataDirectory(), "settings.json")),
				Is.EqualTo("keep me"));
			Assert.That(DataDirectory(), Does.Not.Contain(Path.Combine("versions", "1.1.0")));
		});
	}

	[Test]
	public async Task Plugin_data_survives_a_failed_activation_and_rollback()
	{
		await Install(BuildArtifact());
		WritePluginData("keep me");
		_supervisor.UnhealthyPlugins.Add(PluginId);

		await Install(BuildArtifact("1.1.0", fileName: "plugin-110.macroDeckPlugin"));

		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllText(Path.Combine(DataDirectory(), "settings.json")),
				Is.EqualTo("keep me"));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task Activation_stops_the_previous_version_for_the_update_reason()
	{
		await Install(BuildArtifact());
		_supervisor.Stops.Clear();

		await Install(BuildArtifact("1.1.0", fileName: "plugin-110.macroDeckPlugin"));

		Assert.That(_supervisor.Stops.Select(stop => stop.Reason),
			Does.Contain(Application.Plugins.Runtime.PluginStopReason.Update));
	}

	[Test]
	public async Task Activating_an_older_installed_version_switches_back_to_it()
	{
		await Install(BuildArtifact());
		await Install(BuildArtifact("2.0.0", fileName: "plugin-200.macroDeckPlugin"));

		var result = await _installer.Activate(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Version, Is.EqualTo("1.0.0"));
			Assert.That(result.PreviousVersion, Is.EqualTo("2.0.0"));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("2.0.0")), Is.True);
		});
	}

	[Test]
	public async Task Activating_a_version_that_is_not_installed_leaves_the_active_version_alone()
	{
		await Install(BuildArtifact());

		var result = await _installer.Activate(PluginId, "9.9.9");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
		});
	}

	[Test]
	public async Task Version_pruning_keeps_the_version_a_rollback_would_return_to()
	{
		await Install(BuildArtifact());
		await Install(BuildArtifact("2.0.0", fileName: "plugin-200.macroDeckPlugin"));
		await _installer.Activate(PluginId, "1.0.0");

		await Install(BuildArtifact("3.0.0", fileName: "plugin-300.macroDeckPlugin"));

		Assert.Multiple(() =>
		{
			Assert.That(ActiveVersion(), Is.EqualTo("3.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")),
				Is.True,
				"the previous active version must survive so a rollback has somewhere to go");
		});
	}

	[Test]
	public async Task Version_pruning_never_removes_the_version_current_json_names()
	{
		await Install(BuildArtifact());
		await Install(BuildArtifact("2.0.0", fileName: "plugin-200.macroDeckPlugin"));
		await _installer.Activate(PluginId, "1.0.0");

		await Install(BuildArtifact("3.0.0", fileName: "plugin-300.macroDeckPlugin"));
		await _installer.Activate(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(Directory.Exists(VersionDirectory("1.0.0")), Is.True);
			Assert.That(File.Exists(Path.Combine(VersionDirectory("1.0.0"), "manifest.json")), Is.True);
		});
	}


	private async Task InstallDependency(string dependencyId, string version = "1.0.0")
	{
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, dependencyId, null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"{dependencyId}.macroDeckPlugin");

		var result = await _installer.Install(PluginArtifactSource.FromPath(artifact),
			new PluginInstallRequest());

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public async Task An_unsatisfied_hard_dependency_activates_but_does_not_start_the_plugin()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"dependencies": [ { "id": "com.other.dep", "optional": false } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(result.Warnings.Where(w =>
					w.Severity == PluginInstallWarningSeverity.Blocking &&
					w.SubjectId == "com.other.dep"),
				Is.Not.Empty);
			Assert.That(_supervisor.Starts, Does.Not.Contain(PluginId));
		});
	}

	[Test]
	public async Task An_unsatisfied_optional_dependency_is_advisory_and_the_plugin_still_starts()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"dependencies": [ { "id": "com.other.dep", "optional": true } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(result.Warnings.Any(w => w.SubjectId == "com.other.dep"), Is.True);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	[Test]
	public async Task A_satisfied_hard_dependency_produces_no_blocking_warning_and_starts()
	{
		await InstallDependency("com.other.dep", "2.1.0");

		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"dependencies": [ { "id": "com.other.dep", "versionRange": ">=2.0.0,<3.0.0" } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	[Test]
	public async Task A_hard_dependency_installed_outside_its_declared_range_is_still_unsatisfied()
	{
		await InstallDependency("com.other.dep", "1.5.0");

		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"dependencies": [ { "id": "com.other.dep", "versionRange": ">=2.0.0" } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HasBlockingWarning, Is.True);
			Assert.That(_supervisor.Starts, Does.Not.Contain(PluginId));
		});
	}

	[Test]
	public async Task A_declared_conflict_that_is_installed_blocks_the_start_but_not_the_install()
	{
		await InstallDependency("com.other.rival");

		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"conflicts": [ { "id": "com.other.rival" } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.Warnings.Any(w =>
					w.Severity == PluginInstallWarningSeverity.Blocking &&
					w.SubjectId == "com.other.rival"),
				Is.True);
			Assert.That(_supervisor.Starts, Does.Not.Contain(PluginId));
		});
	}

	[Test]
	public async Task A_declared_conflict_that_is_not_installed_produces_no_warning()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"conflicts": [ { "id": "com.other.rival" } ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	[TestCase("false")]
	[TestCase("true")]
	public async Task An_icon_pack_reference_is_advisory_whether_or_not_it_is_optional(string optional)
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			$$"""
			  "iconPacks": [ { "id": "com.suchbyte.icons", "optional": {{optional}} } ]
			  """);

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(result.Warnings.Any(w => w.SubjectId == "com.suchbyte.icons"), Is.True);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	[Test]
	public async Task An_unknown_permission_is_advisory_and_the_declared_permissions_survive_install()
	{
		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"permissions": [ "host:variables", "totally.made.up" ]
			""");

		var result = await Install(artifact);
		var installedManifest = File.ReadAllText(Path.Combine(VersionDirectory("1.0.0"), "manifest.json"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(result.Warnings.Any(w => w.SubjectId == "totally.made.up"), Is.True);
			Assert.That(installedManifest, Does.Contain("totally.made.up"));
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	[Test]
	public async Task No_combination_of_warnings_makes_an_install_fail()
	{
		await InstallDependency("com.other.rival");

		var artifact = BuildArtifact(extraManifestBlocks:
			"""
			"dependencies": [
				{ "id": "com.other.missing", "optional": false },
				{ "id": "com.other.wanted", "optional": true }
			],
			"conflicts": [ { "id": "com.other.rival" } ],
			"iconPacks": [ { "id": "com.suchbyte.icons", "optional": false } ],
			"permissions": [ "made.up" ]
			""");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Error, Is.Null);
			Assert.That(result.Activated, Is.True);
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
		});
	}


	[Test]
	public async Task Uninstalling_removes_the_versions_and_the_active_marker()
	{
		await Install(BuildArtifact());

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(Directory.Exists(PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
			Assert.That(ActiveVersion(), Is.Null);
		});
	}

	[Test]
	public async Task Uninstalling_keeps_plugin_data_by_default_and_removes_it_on_request()
	{
		await Install(BuildArtifact());
		WritePluginData("keep me");

		await _installer.Uninstall(PluginId, new PluginUninstallRequest());
		Assert.That(File.ReadAllText(Path.Combine(DataDirectory(), "settings.json")), Is.EqualTo("keep me"));

		await Install(BuildArtifact(fileName: "again.macroDeckPlugin"));
		await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)), Is.False);
	}

	[Test]
	public async Task Uninstalling_is_refused_while_an_installed_plugin_hard_depends_on_it()
	{
		await InstallDependency("com.other.dep");

		var dependent = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				"""
				"dependencies": [ { "id": "com.other.dep", "optional": false } ]
				"""))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, "dependent.macroDeckPlugin");

		await Install(dependent);

		var result = await _installer.Uninstall("com.other.dep", new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.DependencyInUse));
			Assert.That(result.ErrorMessage, Does.Contain(PluginId));
			Assert.That(
				Directory.Exists(PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, "com.other.dep")),
				Is.True);
		});
	}

	[Test]
	public async Task An_optional_dependent_does_not_block_an_uninstall()
	{
		await InstallDependency("com.other.dep");

		var dependent = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build("1.0.0",
				PluginId,
				"""
				"dependencies": [ { "id": "com.other.dep", "optional": true } ]
				"""))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, "dependent.macroDeckPlugin");

		await Install(dependent);

		var result = await _installer.Uninstall("com.other.dep", new PluginUninstallRequest());

		Assert.That(result.Success, Is.True, result.ErrorMessage);
	}

	[Test]
	public async Task Uninstalling_a_plugin_that_is_not_installed_reports_not_installed()
	{
		var result = await _installer.Uninstall("com.suchbyte.nope", new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
		});
	}

	// The two tests below prove that MacroDeck.Plugin.Cli's own `validate` command (issue #611 part 2) added
	// package/publication-level checks - publication-metadata-missing, icon-declared-not-present,
	// entrypoint-not-packed - purely as tooling, without the real installer starting to consult them. The
	// host keeps installing and activating exactly what it always did; nothing here should ever regress
	// because the CLI got stricter.

	[Test]
	public async Task A_manifest_with_only_the_five_required_fields_still_installs_and_activates()
	{
		// ManifestJson.Build already writes exactly the five required fields (manifestVersion, id, name,
		// version, entrypoints) across every declared runtime identifier, with no
		// publisher/description/icon/license/repository/compatibility/files/signature - and BuildArtifact
		// backs only the current host's entrypoint with a real file, leaving the other declared runtime
		// identifiers as bona fide foreign RIDs.
		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));

			Assert.That(result.Warnings,
				Has.None.Matches<PluginInstallWarning>(warning =>
					_publicationVocabulary.Any(word =>
						warning.Code.Contains(word, StringComparison.OrdinalIgnoreCase) ||
						warning.Message.Contains(word, StringComparison.OrdinalIgnoreCase))));
		});
	}

	[Test]
	public async Task The_host_installer_never_consults_a_validation_level_above_development()
	{
		// A declared icon that does not exist, and two declared runtime identifiers this build never backed
		// with a file - both defects MacroDeck.Plugin.Cli's `validate --level package` now reports
		// (icon-declared-not-present, entrypoint-not-packed), and neither the manifest reader nor the
		// installer has ever checked.
		var artifact = BuildArtifact(extraManifestBlocks: """"
														  "icon": "assets/missing-icon.png"
														  """");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(_supervisor.Starts, Does.Contain(PluginId));
		});
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
			=> throw new InvalidOperationException("A unit test must not reach the network.");
	}
}
