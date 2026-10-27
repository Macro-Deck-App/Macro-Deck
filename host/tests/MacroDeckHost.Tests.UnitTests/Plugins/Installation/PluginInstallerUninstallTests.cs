using System.Text.Json;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallerUninstallTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private TestPaths _paths = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private PluginInstaller _installer = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private InMemoryPluginAccessTokenRepository _accessTokens = null!;
	private FakeIntegrationRegistrar _integrationRegistrar = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private PluginCompatibilityService _compatibility = null!;
	private PluginInstallationCatalog _catalog = null!;
	private JsonStoreTestInstallationStore _testInstallations = null!;
	private string _sourceDirectory = null!;
	private InMemoryConfigEntryRepository _configEntries = null!;
	private FakeSecretService _secrets = null!;
	private IntegrationConfigStore _configStore = null!;
	private RemotePluginSnapshotStore _snapshots = null!;

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
		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with
		{
			ActivationHealthTimeout = TimeSpan.FromSeconds(2)
		};

		_registrations = new InMemoryPluginRegistrationRepository();
		_accessTokens = new InMemoryPluginAccessTokenRepository();
		_integrationRegistrar = new FakeIntegrationRegistrar();
		_sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(_catalog, _sessionRegistry);

		_compatibility = new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance);
		var logRateLimiter = new PluginLogRateLimiter(TimeProvider.System);
		var forgetter = new PluginIdentityForgetter(_compatibility,
			logRateLimiter,
			new PluginLogIngestor(logRateLimiter, _sessionRegistry, new FakePluginSupervisor(), TimeProvider.System));

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository>(_registrations);
		services.AddSingleton<IPluginAccessTokenRepository>(_accessTokens);
		services.AddSingleton<IPluginRegistrationService>(_ => new PluginRegistrationService(_registrations,
			_accessTokens,
			_sessionRegistry,
			forgetter,
			_catalog,
			new PluginTakeoverRegistry(),
			TimeProvider.System));
		services.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		_testInstallations = new JsonStoreTestInstallationStore(_paths, Serilog.Core.Logger.None);
		services.AddSingleton<IStoreTestInstallationStore>(_testInstallations);
		_configEntries = new InMemoryConfigEntryRepository();
		_secrets = new FakeSecretService();
		_configStore = new IntegrationConfigStore(_configEntries, _secrets);
		_snapshots = new RemotePluginSnapshotStore(_paths, Serilog.Core.Logger.None);
		services.AddSingleton<IIntegrationConfigEntryRepository>(_configEntries);
		services.AddSingleton<ISecretService>(_secrets);
		services.AddScoped<IIntegrationConfigStore, IntegrationConfigStore>();
		services.AddSingleton<IRemotePluginSnapshotStore>(_snapshots);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
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
			_sessionRegistry,
			new PluginTakeoverRegistry(),
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_snapshots.Dispose();
		_paths.Cleanup();
	}

	private string BuildArtifact(
		string version = "1.0.0",
		string? extraManifestBlocks = null,
		string fileName = "plugin.macroDeckPlugin",
		string pluginId = PluginId)
	{
		return new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, extraManifestBlocks))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, fileName);
	}

	private Task<PluginInstallResult> Install(string artifactPath, PluginInstallRequest? request = null)
	{
		return _installer.Install(PluginArtifactSource.FromPath(artifactPath),
			request ?? new PluginInstallRequest());
	}

	private PluginAccessTokenEntity SeedAccessToken()
	{
		var token = new PluginAccessTokenEntity
		{
			Id = Guid.NewGuid(),
			CreatedAt = DateTime.UtcNow,
			Name = "developer-token",
			TokenHash = "token-hash",
			Scopes = "plugin:enroll"
		};

		_accessTokens.Tokens.Add(token);
		return token;
	}

	private PluginRegistrationEntity SeedRegistration(Guid accessTokenId, string pluginId = PluginId)
	{
		var registration = new PluginRegistrationEntity
		{
			Id = Guid.NewGuid(),
			CreatedAt = DateTime.UtcNow,
			PluginId = pluginId,
			DisplayName = "Test Plugin",
			SecretHash = "secret-hash",
			AccessTokenId = accessTokenId,
			Origin = PluginRegistrationOrigins.DeveloperToken
		};

		_registrations.Registrations.Add(registration);
		return registration;
	}


	[Test]
	public async Task Uninstalling_revokes_the_registration_but_keeps_the_row_so_a_reinstall_keeps_its_identity()
	{
		await Install(BuildArtifact());
		var accessToken = SeedAccessToken();
		SeedRegistration(accessToken.Id);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.That(result.Success, Is.True, result.ErrorMessage);

		var registration = _registrations.Registrations.Single(r => r.PluginId == PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(registration,
				Is.Not.Null,
				"the registration row must survive uninstall so a reinstall keeps the same identity");
			Assert.That(registration.RevokedAt,
				Is.Not.Null,
				"the plugin's own credential must be revoked so nothing that authenticates as it outlives it");
		});
	}

	[Test]
	public async Task Uninstalling_never_touches_the_users_developer_access_token()
	{
		await Install(BuildArtifact());
		var accessToken = SeedAccessToken();
		SeedRegistration(accessToken.Id);

		await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		var storedToken = _accessTokens.Tokens.Single(t => t.Id == accessToken.Id);

		Assert.That(storedToken.RevokedAt, Is.Null);
	}

	[Test]
	public async Task Uninstalling_a_plugin_with_no_registration_row_does_not_throw()
	{
		await Install(BuildArtifact());

		PluginInstallResult? result = null;
		Assert.DoesNotThrowAsync(async () =>
			result = await _installer.Uninstall(PluginId, new PluginUninstallRequest()));

		Assert.That(result!.Success, Is.True, result.ErrorMessage);
	}

	// --- uninstalling what is already half gone ---------------------------------------------------

	private DateTime? RevokedAtOf(string pluginId)
	{
		return _registrations.Registrations.Single(registration => registration.PluginId == pluginId).RevokedAt;
	}

	[Test]
	public async Task Uninstalling_a_plugin_whose_folder_was_deleted_behind_the_hosts_back_retires_its_host_state()
	{
		var token = SeedAccessToken();
		SeedRegistration(token.Id);
		await Install(BuildArtifact(), new PluginInstallRequest { StartAfterActivation = false });
		await _supervisor.Start(PluginId);
		Directory.Delete(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId), true);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(RevokedAtOf(PluginId), Is.Not.Null);
			Assert.That(_integrationRegistrar.IsRegistered(PluginId), Is.False);
		});
	}

	[Test]
	public async Task An_uninstall_that_only_got_as_far_as_the_files_finishes_on_the_next_attempt()
	{
		var token = SeedAccessToken();
		SeedRegistration(token.Id);
		await Install(BuildArtifact(), new PluginInstallRequest { StartAfterActivation = false });

		// What a host killed mid-uninstall leaves behind: the versions are gone, everything the host
		// tracks about the plugin is not.
		Directory.Delete(PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, PluginId), true);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(RevokedAtOf(PluginId), Is.Not.Null);
			Assert.That(_integrationRegistrar.IsRegistered(PluginId), Is.False);
			Assert.That(_supervisor.DesiredStartState.ContainsKey(PluginId), Is.False);
			Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
		});
	}

	[Test]
	public async Task Uninstalling_forgets_the_test_build_the_plugin_was_installed_from()
	{
		await Install(BuildArtifact("1.2.0"));
		_testInstallations.Save(new StoreTestInstallationRecord
		{
			PluginId = PluginId,
			Version = "1.2.0",
			Build = "42",
			BuildId = Guid.NewGuid(),
			ArtifactSha256 = new string('0', 64)
		});

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(_testInstallations.Find(PluginId), Is.Null);
		});
	}

	[Test]
	public async Task Keeping_the_data_of_a_plugin_that_has_none_stops_it_being_reported_as_installed()
	{
		await Install(BuildArtifact());

		await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = true });

		Assert.That(_catalog.Discover().Select(plugin => plugin.PluginId), Does.Not.Contain(PluginId));
	}

	[Test]
	public async Task Keeping_the_data_of_a_plugin_that_has_some_keeps_the_data()
	{
		await Install(BuildArtifact());
		var dataDirectory = PluginInstallPaths.DataDirectory(_paths.PluginsDirectory, PluginId);
		Directory.CreateDirectory(dataDirectory);
		await File.WriteAllTextAsync(Path.Combine(dataDirectory, "settings.json"), "{}");

		await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = true });

		Assert.That(File.Exists(Path.Combine(dataDirectory, "settings.json")), Is.True);
	}

	[Test]
	public async Task A_plugin_id_with_nothing_left_to_remove_is_still_reported_as_not_installed()
	{
		await Install(BuildArtifact());
		await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
	}

	[Test]
	public async Task Uninstalling_an_id_that_is_only_enrolled_does_not_revoke_that_enrollment()
	{
		var token = SeedAccessToken();
		SeedRegistration(token.Id, "com.example.developer-build");

		var result = await _installer.Uninstall("com.example.developer-build", new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
			Assert.That(RevokedAtOf("com.example.developer-build"), Is.Null);
		});
	}

	[Test]
	public async Task Uninstalling_an_id_a_connected_developer_build_is_enrolled_under_leaves_it_running()
	{
		var token = SeedAccessToken();
		SeedRegistration(token.Id, "com.example.developer-build");
		await CreateSession("com.example.developer-build");

		var result = await _installer.Uninstall("com.example.developer-build", new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
			Assert.That(RevokedAtOf("com.example.developer-build"), Is.Null);
			Assert.That(_sessionRegistry.Snapshot(), Is.Not.Empty, "the developer build's session must survive");
		});
	}

	[Test]
	public async Task An_id_that_is_not_a_plugin_id_reaches_no_directory()
	{
		var neighbour = Path.Combine(_paths.BaseDirectory, "neighbour");
		Directory.CreateDirectory(neighbour);

		var result = await _installer.Uninstall(Path.Combine("..", "neighbour"),
			new PluginUninstallRequest { KeepData = false });

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.NotInstalled));
			Assert.That(Directory.Exists(neighbour), Is.True);
		});
	}

	// --- the host state a removed plugin must not keep --------------------------------------------

	private void RecordCompatibility(string pluginId)
	{
		_compatibility.Record(new PluginCompatibilityEvaluation
		{
			PluginId = pluginId,
			DisplayName = pluginId,
			NegotiatedProtocolVersion = 1
		});
	}

	private Task CreateSession(string pluginId)
	{
		return _sessionRegistry.Create(new PluginSessionRecord
		{
			SessionId = $"session-{pluginId}",
			PluginId = pluginId,
			DisplayName = pluginId,
			AccessTokenId = Guid.NewGuid(),
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Connected,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
	}

	[Test]
	public async Task Uninstalling_forgets_only_that_plugins_compatibility_verdict()
	{
		await Install(BuildArtifact());
		await Install(BuildArtifact(fileName: "other.macroDeckPlugin", pluginId: "com.example.other"));
		RecordCompatibility(PluginId);
		RecordCompatibility("com.example.other");

		await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(_compatibility.Find(PluginId), Is.Null);
			Assert.That(_compatibility.Find("com.example.other"), Is.Not.Null);
		});
	}

	[Test]
	public async Task Uninstalling_ends_the_plugins_session()
	{
		await Install(BuildArtifact());
		await CreateSession(PluginId);

		await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.That(_sessionRegistry.Snapshot(), Is.Empty);
	}

	[Test]
	public async Task Uninstalling_drops_the_plugins_desired_start_state()
	{
		await Install(BuildArtifact());

		await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.That(_supervisor.DesiredStartState.ContainsKey(PluginId), Is.False);
	}


	[Test]
	public async Task
		An_artifact_with_no_entrypoint_this_host_can_use_installs_with_an_advisory_warning_and_does_not_start()
	{
		var artifact = new PluginArtifactBuilder()
			.WithManifest($$"""
							{
								"manifestVersion": 1,
								"id": "{{PluginId}}",
								"name": "Test Plugin",
								"version": "1.0.0",
								"entrypoints": {
									"solaris-sparc64": { "executable": "{{ManifestJson.EntrypointExecutable}}" }
								}
							}
							""")
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, "no-usable-entrypoint.macroDeckPlugin");

		var result = await Install(artifact);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(result.Activated, Is.True);
			Assert.That(result.HasBlockingWarning, Is.False);
			Assert.That(_supervisor.Starts, Does.Not.Contain(PluginId));
		});
	}


	[Test]
	public async Task An_install_that_is_not_started_still_appears_as_an_integration()
	{
		var result = await Install(BuildArtifact(),
			new PluginInstallRequest { StartAfterActivation = false });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(_supervisor.Starts, Does.Not.Contain(PluginId));
			Assert.That(_integrationRegistrar.IsRegistered(PluginId), Is.True);
		});
	}

	[Test]
	public async Task Uninstalling_removes_the_plugins_integration_even_when_it_never_ran()
	{
		await Install(BuildArtifact(), new PluginInstallRequest { StartAfterActivation = false });
		var registeredBeforeUninstall = _integrationRegistrar.IsRegistered(PluginId);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(registeredBeforeUninstall,
				Is.True,
				"the plugin must have an integration before uninstalling can be shown to remove it");
			Assert.That(_integrationRegistrar.IsRegistered(PluginId), Is.False);
		});
	}

	// --- what deleting the stored data deletes ---------------------------------------------------

	private async Task<(Guid EntryId, Guid SecretId)> SeedConnection(string integrationId)
	{
		var secretId = _secrets.Store("api-token");
		var values = new Dictionary<string, JsonElement>
		{
			["host"] = JsonSerializer.SerializeToElement("example.local"),
			["token"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
			{
				[SecretReferenceJson.PropertyName] = secretId.ToString()
			})
		};

		var entryId = await _configStore.Create(integrationId, "Account", values);
		return (entryId, secretId);
	}

	[Test]
	public async Task Deleting_the_data_deletes_the_plugins_connections_and_their_credentials_but_no_one_elses()
	{
		await Install(BuildArtifact());
		var own = await SeedConnection(PluginId);
		var other = await SeedConnection("com.example.other");

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(await _configStore.List(PluginId), Is.Empty);
			Assert.That(await _secrets.Resolve(own.SecretId), Is.Null);
			Assert.That((await _configStore.List("com.example.other")).Select(entry => entry.Id),
				Is.EqualTo(new[] { other.EntryId }));
			Assert.That(await _secrets.Resolve(other.SecretId), Is.EqualTo("api-token"));
		});
	}

	[Test]
	public async Task Keeping_the_data_keeps_the_plugins_connections_and_their_credentials()
	{
		await Install(BuildArtifact());
		var own = await SeedConnection(PluginId);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = true });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That((await _configStore.List(PluginId)).Select(entry => entry.Id),
				Is.EqualTo(new[] { own.EntryId }));
			Assert.That(await _secrets.Resolve(own.SecretId), Is.EqualTo("api-token"));
		});
	}

	[Test]
	public async Task Deleting_the_data_forgets_the_capabilities_the_host_remembered_for_the_plugin()
	{
		await Install(BuildArtifact());
		await _snapshots.SaveAsync(RemotePluginCapabilitySnapshot.Empty(PluginId));
		await _snapshots.SaveAsync(RemotePluginCapabilitySnapshot.Empty("com.example.other"));

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		using var reloaded = new RemotePluginSnapshotStore(_paths, Serilog.Core.Logger.None);
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(_snapshots.Has(PluginId), Is.False);
			Assert.That(reloaded.Has(PluginId), Is.False, "the forgotten snapshot must not come back after a restart");
			Assert.That(reloaded.Has("com.example.other"), Is.True);
		});
	}

	[Test]
	public async Task Data_that_cannot_be_deleted_leaves_the_plugin_installed_so_the_user_can_try_again()
	{
		Assume.That(OperatingSystem.IsWindows() || Environment.UserName != "root",
			"root ignores the read-only folder this test relies on");
		await Install(BuildArtifact(), new PluginInstallRequest { StartAfterActivation = false });
		var own = await SeedConnection(PluginId);
		await _snapshots.SaveAsync(RemotePluginCapabilitySnapshot.Empty(PluginId));
		var dataDirectory = PluginInstallPaths.DataDirectory(_paths.PluginsDirectory, PluginId);
		Directory.CreateDirectory(dataDirectory);
		var dataFile = Path.Combine(dataDirectory, "settings.json");
		await File.WriteAllTextAsync(dataFile, "{}");

		PluginInstallResult blocked;
		using (LockAgainstDeletion(dataDirectory, dataFile))
		{
			blocked = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });
		}

		var stillInstalled
			= _catalog.Discover().Any(plugin => plugin.PluginId == PluginId && plugin.Versions.Count > 0);
		var stillRegistered = _integrationRegistrar.IsRegistered(PluginId);
		var connectionsAfterTheFailedAttempt = (await _configStore.List(PluginId)).Count;
		var retried = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(blocked.Success, Is.False, "data that is still there must not be reported as deleted");
			Assert.That(blocked.Error, Is.EqualTo(PluginInstallError.Failed));
			Assert.That(blocked.ErrorMessage, Is.Not.Empty);
			Assert.That(stillInstalled, Is.True, "the dialog the user retries from needs the plugin to still be there");
			Assert.That(stillRegistered, Is.True);
			Assert.That(connectionsAfterTheFailedAttempt, Is.EqualTo(1));
			Assert.That(retried.Success, Is.True, retried.ErrorMessage);
			Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
			Assert.That(await _configStore.List(PluginId), Is.Empty);
			Assert.That(await _secrets.Resolve(own.SecretId), Is.Null);
			Assert.That(_snapshots.Has(PluginId), Is.False);
		});
	}

	[Test]
	public async Task A_plugin_that_cannot_be_stopped_is_not_uninstalled()
	{
		await Install(BuildArtifact());
		var versionsDirectory = PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, PluginId);
		_supervisor.StopOutcome = _ => PluginSupervisorResult.Fail(PluginSupervisorError.Failed, "terminate failed");

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PluginInstallError.Failed));
			Assert.That(Directory.Exists(versionsDirectory),
				Is.True,
				"nothing may be deleted from under a process that may still be running");
		});
	}

	[TestCase(PluginSupervisorError.NotRunning)]
	[TestCase(PluginSupervisorError.NotInstalled)]
	[TestCase(PluginSupervisorError.SelfRegistering)]
	public async Task A_plugin_that_was_not_running_is_still_uninstalled(PluginSupervisorError stopError)
	{
		await Install(BuildArtifact());
		_supervisor.StopOutcome = _ => PluginSupervisorResult.Fail(stopError);

		var result = await _installer.Uninstall(PluginId, new PluginUninstallRequest { KeepData = false });

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);
			Assert.That(Directory.Exists(PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, PluginId)),
				Is.False);
		});
	}

	// Windows refuses to delete a file another handle holds; elsewhere a read-only folder refuses it.
	private static IDisposable LockAgainstDeletion(string directory, string file)
	{
		if (OperatingSystem.IsWindows())
		{
			return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
		}

		var mode = File.GetUnixFileMode(directory);
		File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
		return new RestoreUnixFileMode(directory, mode);
	}

	private sealed class RestoreUnixFileMode : IDisposable
	{
		private readonly string _path;
		private readonly UnixFileMode _mode;

		public RestoreUnixFileMode(string path, UnixFileMode mode)
		{
			_path = path;
			_mode = mode;
		}

		public void Dispose()
		{
			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(_path, _mode);
			}
		}
	}

	private sealed class InMemoryConfigEntryRepository : IIntegrationConfigEntryRepository
	{
		private readonly List<IntegrationConfigEntryEntity> _entries = [];

		public Task<IReadOnlyList<IntegrationConfigEntryEntity>> GetByIntegrationId(string integrationId)
		{
			return Task.FromResult<IReadOnlyList<IntegrationConfigEntryEntity>>(_entries
				.Where(entry => entry.IntegrationId == integrationId)
				.ToList());
		}

		public Task<IntegrationConfigEntryEntity?> GetById(Guid id)
		{
			return Task.FromResult(_entries.FirstOrDefault(entry => entry.Id == id));
		}

		public Task Create(IntegrationConfigEntryEntity entry)
		{
			_entries.Add(entry);
			return Task.CompletedTask;
		}

		public Task Update(IntegrationConfigEntryEntity entry)
		{
			return Task.CompletedTask;
		}

		public Task TryDeleteById(Guid id)
		{
			_entries.RemoveAll(entry => entry.Id == id);
			return Task.CompletedTask;
		}
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
		{
			throw new InvalidOperationException("A unit test must not reach the network.");
		}
	}
}
