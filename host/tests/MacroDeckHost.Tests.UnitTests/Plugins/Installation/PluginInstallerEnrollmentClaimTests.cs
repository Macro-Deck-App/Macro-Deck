using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallerEnrollmentClaimTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private TestPaths _paths = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private PluginInstaller _installer = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private InMemoryPluginAccessTokenRepository _accessTokens = null!;
	private FakeIntegrationRegistrar _integrationRegistrar = null!;
	private PluginSessionRegistry _sessionRegistry = null!;
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

		var catalog = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with
		{
			ActivationHealthTimeout = TimeSpan.FromSeconds(2)
		};

		_registrations = new InMemoryPluginRegistrationRepository();
		_accessTokens = new InMemoryPluginAccessTokenRepository();
		_integrationRegistrar = new FakeIntegrationRegistrar();
		_sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(catalog, _sessionRegistry);

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository>(_registrations);
		services.AddSingleton<IPluginAccessTokenRepository>(_accessTokens);
		services.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		var provider = services.BuildServiceProvider();

		_installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(),
				options,
				Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator(),
			new PluginDependencyResolver(catalog, manifestReader),
			manifestReader,
			catalog,
			_supervisor,
			_integrationRegistrar,
			_sessionRegistry,
			provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private string BuildArtifact(string pluginId = PluginId, string version = "1.0.0")
	{
		return new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, pluginId, extraBlocks: null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"{pluginId}.macroDeckPlugin");
	}

	private Task<PluginInstallResult> Install(string artifactPath, PluginInstallRequest? request = null)
	{
		return _installer.Install(PluginArtifactSource.FromPath(artifactPath), request ?? new PluginInstallRequest());
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
			DisplayName = "Developer Build",
			SecretHash = "secret-hash",
			AccessTokenId = accessTokenId,
			Origin = PluginRegistrationOrigins.DeveloperToken
		};

		_registrations.Registrations.Add(registration);
		return registration;
	}

	private async Task<FakePluginConnection> AttachSession(string pluginId)
	{
		var sessionId = $"session-{pluginId}-{Guid.NewGuid():N}";
		var record = new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = "Developer Build",
			Origin = PluginSessionOrigin.SelfRegistered,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			CreatedAt = DateTimeOffset.UtcNow
		};

		await _sessionRegistry.Create(record);

		var connection = new FakePluginConnection();
		_sessionRegistry.TryAttach(sessionId, connection, instanceId: null);
		return connection;
	}

	[Test]
	public async Task Installing_a_plugin_revokes_an_existing_enrollment_and_disconnects_its_session()
	{
		var accessToken = SeedAccessToken();
		SeedRegistration(accessToken.Id);
		var connection = await AttachSession(PluginId);

		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);

			var registration = _registrations.Registrations.Single(r => r.PluginId == PluginId);
			Assert.That(registration.RevokedAt, Is.Not.Null);

			Assert.That(_sessionRegistry.Snapshot().Any(s => s.PluginId == PluginId), Is.False);

			Assert.That(connection.Closes, Has.Count.EqualTo(1));
			Assert.That(connection.Closes[0].CloseCode, Is.EqualTo(ProtocolCloseCodes.RegistrationRejected));
		});
	}

	[Test]
	public async Task Installing_a_plugin_leaves_an_unrelated_enrollment_alone()
	{
		const string otherPluginId = "com.example.other";

		var accessToken = SeedAccessToken();
		SeedRegistration(accessToken.Id, otherPluginId);
		var otherConnection = await AttachSession(otherPluginId);

		var result = await Install(BuildArtifact());

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True, result.ErrorMessage);

			var otherRegistration = _registrations.Registrations.Single(r => r.PluginId == otherPluginId);
			Assert.That(otherRegistration.RevokedAt, Is.Null);

			Assert.That(_sessionRegistry.Snapshot().Any(s => s.PluginId == otherPluginId), Is.True);
			Assert.That(otherConnection.Closes, Is.Empty);
		});
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
			=> throw new InvalidOperationException("A unit test must not reach the network.");
	}
}
