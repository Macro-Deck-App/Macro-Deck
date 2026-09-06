using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginIdentityReconciliationTests
{
	private ManualTimeProvider _time = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private InMemoryPluginAccessTokenRepository _tokens = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
	private FakePluginInstallationCatalog _catalog = null!;
	private PluginRegistrationService _registrationService = null!;
	private PluginIdentityReconciler _reconciler = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_registrations = new InMemoryPluginRegistrationRepository();
		_tokens = new InMemoryPluginAccessTokenRepository();
		_sessionRegistry = new PluginSessionRegistry(_time, Serilog.Core.Logger.None);
		_catalog = new FakePluginInstallationCatalog();

		var logRateLimiter = new PluginLogRateLimiter(_time);
		var logIngestor = new PluginLogIngestor(logRateLimiter, _sessionRegistry, new FakePluginSupervisor(), _time);
		_registrationService = new PluginRegistrationService(_registrations,
			_tokens,
			_sessionRegistry,
			new PluginIdentityForgetter(new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance),
				logRateLimiter,
				logIngestor),
			_catalog,
			_time);

		_reconciler = new PluginIdentityReconciler(_registrations,
			_catalog,
			_registrationService,
			Serilog.Core.Logger.None);
	}

	private Guid SeedAccessToken()
	{
		var accessTokenId = Guid.NewGuid();
		_tokens.Tokens.Add(new PluginAccessTokenEntity
		{
			Id = accessTokenId,
			Name = "Dev Token",
			TokenHash = "irrelevant",
			Scopes = PluginTokenScopes.Enroll,
			CreatedAt = _time.GetUtcNow().UtcDateTime
		});

		return accessTokenId;
	}

	private async Task<PluginSessionRecord> AttachSession(string pluginId)
	{
		var record = new PluginSessionRecord
		{
			SessionId = Guid.CreateVersion7().ToString("D"),
			PluginId = pluginId,
			DisplayName = "Developer Build",
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = _time.GetUtcNow()
		};

		await _sessionRegistry.Create(record);
		_sessionRegistry.TryAttach(record.SessionId, new FakePluginConnection(), instanceId: null);
		return record;
	}

	private static InstalledPlugin InstalledWith(string pluginId, string version = "1.0.0")
	{
		var installedVersion = new InstalledPluginVersion
		{
			Version = version,
			VersionDirectory = $"/plugins/{pluginId}/versions/{version}",
			ManifestPath = $"/plugins/{pluginId}/versions/{version}/manifest.json"
		};

		return new InstalledPlugin
		{
			PluginId = pluginId,
			PluginDirectory = $"/plugins/{pluginId}",
			Versions = [installedVersion],
			ActiveVersion = installedVersion
		};
	}

	[Test]
	public async Task Startup_reconciliation_revokes_an_enrollment_for_an_installed_id_and_drops_its_session()
	{
		var accessTokenId = SeedAccessToken();
		await _registrationService.Register("com.example.plugin",
			"Example",
			accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await AttachSession("com.example.plugin");
		_catalog.Plugins.Add(InstalledWith("com.example.plugin"));

		await _reconciler.ReconcileAsync(CancellationToken.None);

		var registration = _registrations.Registrations.Single(r => r.PluginId == "com.example.plugin");

		Assert.Multiple(() =>
		{
			Assert.That(registration.RevokedAt, Is.Not.Null);
			Assert.That(_sessionRegistry.Snapshot().Any(s => s.PluginId == "com.example.plugin"), Is.False);
		});
	}

	[Test]
	public async Task Startup_reconciliation_leaves_a_re_enrollment_alone_when_the_catalog_entry_has_no_versions()
	{
		var accessTokenId = SeedAccessToken();
		await _registrationService.Register("com.example.plugin",
			"Example",
			accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await AttachSession("com.example.plugin");
		_catalog.Plugins.Add(new InstalledPlugin
		{
			PluginId = "com.example.plugin",
			PluginDirectory = "/plugins/com.example.plugin",
			Versions = [],
			ActiveVersion = null
		});

		await _reconciler.ReconcileAsync(CancellationToken.None);

		var registration = _registrations.Registrations.Single(r => r.PluginId == "com.example.plugin");

		Assert.Multiple(() =>
		{
			Assert.That(registration.RevokedAt, Is.Null);
			Assert.That(_sessionRegistry.Snapshot().Any(s => s.PluginId == "com.example.plugin"), Is.True);
		});
	}

	[Test]
	public async Task Startup_reconciliation_leaves_an_enrollment_with_no_installation_alone()
	{
		var accessTokenId = SeedAccessToken();
		await _registrationService.Register("com.example.plugin",
			"Example",
			accessTokenId,
			PluginRegistrationOrigins.DeveloperToken);
		await AttachSession("com.example.plugin");
		_catalog.Plugins.Add(InstalledWith("com.example.other"));

		await _reconciler.ReconcileAsync(CancellationToken.None);

		var registration = _registrations.Registrations.Single(r => r.PluginId == "com.example.plugin");

		Assert.Multiple(() =>
		{
			Assert.That(registration.RevokedAt, Is.Null);
			Assert.That(_sessionRegistry.Snapshot().Any(s => s.PluginId == "com.example.plugin"), Is.True);
		});
	}
}
