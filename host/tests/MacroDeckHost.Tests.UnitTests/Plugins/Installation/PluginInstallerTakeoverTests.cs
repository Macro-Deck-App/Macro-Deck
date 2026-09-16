using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Compatibility;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginInstallerTakeoverTests
{
	private const string PluginId = ManifestJson.DefaultPluginId;

	private TestPaths _paths = null!;
	private PluginInstallationCatalog _catalog = null!;
	private FakeInstallSupervisor _supervisor = null!;
	private PluginTakeoverRegistry _takeovers = null!;
	private InMemoryPluginRegistrationRepository _registrations = null!;
	private ServiceProvider _provider = null!;
	private PluginInstaller _installer = null!;
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
		_takeovers = new PluginTakeoverRegistry();
		_registrations = new InMemoryPluginRegistrationRepository();
		var accessTokens = new InMemoryPluginAccessTokenRepository();
		var manifestReader = new PluginManifestReader();
		var options = PluginInstallerOptions.Default with { ActivationHealthTimeout = TimeSpan.FromSeconds(2) };
		var sessionRegistry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_supervisor = new FakeInstallSupervisor(_catalog, sessionRegistry);

		var logRateLimiter = new PluginLogRateLimiter(TimeProvider.System);
		var forgetter = new PluginIdentityForgetter(
			new PluginCompatibilityService(NullLogger<PluginCompatibilityService>.Instance),
			logRateLimiter,
			new PluginLogIngestor(logRateLimiter, sessionRegistry, new FakePluginSupervisor(), TimeProvider.System));

		var services = new ServiceCollection();
		services.AddSingleton<IPluginRegistrationRepository>(_registrations);
		services.AddSingleton<IPluginAccessTokenRepository>(accessTokens);
		services.AddSingleton<IPluginTrustRecordRepository, InMemoryPluginTrustRecordRepository>();
		services.AddSingleton<IPluginTrustBaseline, FakePluginTrustBaseline>();
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		services.AddSingleton<IPluginRegistrationService>(_ => new PluginRegistrationService(_registrations,
			accessTokens,
			sessionRegistry,
			forgetter,
			_catalog,
			_takeovers,
			TimeProvider.System));
		_provider = services.BuildServiceProvider();

		_installer = new PluginInstaller(_paths,
			new PluginArtifactReader(manifestReader, Serilog.Core.Logger.None),
			new PluginArtifactAcquirer(new NoHttpClientFactory(), options, Serilog.Core.Logger.None),
			new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None),
			new FakePluginTrustEvaluator(),
			new PluginDependencyResolver(_catalog, manifestReader),
			manifestReader,
			_catalog,
			_supervisor,
			new FakeIntegrationRegistrar(),
			sessionRegistry,
			_takeovers,
			_provider.GetRequiredService<IServiceScopeFactory>(),
			options,
			TimeProvider.System,
			Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_provider.Dispose();
		_paths.Cleanup();
	}

	[Test]
	public async Task An_update_of_a_taken_over_plugin_is_refused_before_anything_is_stopped()
	{
		await InstallVersion("1.0.0");
		await BeginTakeover();
		var stopsBefore = _supervisor.Stops.Count;

		var update = await InstallVersion("2.0.0", expectSuccess: false);

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.False);
			Assert.That(update.BlockedByDevelopmentTakeover, Is.True);
			Assert.That(update.ErrorMessage,
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Errors.Plugins.InstallBlockedByTakeover())),
				"the desktop shows this message as it is");
			Assert.That(_supervisor.Stops, Has.Count.EqualTo(stopsBefore));
			Assert.That(ActiveVersion(), Is.EqualTo("1.0.0"));
			Assert.That(_takeovers.IsActive(PluginId), Is.True);
		});
	}

	[Test]
	public async Task Activating_a_version_of_a_taken_over_plugin_is_refused()
	{
		await InstallVersion("1.0.0");
		await BeginTakeover();
		var stopsBefore = _supervisor.Stops.Count;

		var activation = await _installer.Activate(PluginId, "1.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(activation.Success, Is.False);
			Assert.That(activation.BlockedByDevelopmentTakeover, Is.True);
			Assert.That(_supervisor.Stops, Has.Count.EqualTo(stopsBefore));
			Assert.That(_takeovers.IsActive(PluginId), Is.True);
		});
	}

	[Test]
	public async Task Uninstalling_a_taken_over_plugin_ends_the_takeover_and_its_credential()
	{
		await InstallVersion("1.0.0");
		await BeginTakeover();

		var uninstall = await _installer.Uninstall(PluginId, new PluginUninstallRequest());

		Assert.Multiple(() =>
		{
			Assert.That(uninstall.Success, Is.True, uninstall.ErrorMessage);
			Assert.That(_takeovers.IsActive(PluginId), Is.False);
			Assert.That(_registrations.Registrations.Any(r => r.PluginId == PluginId && r.RevokedAt is null), Is.False);
		});
	}

	[Test]
	public async Task A_takeover_that_begins_while_an_update_activates_does_not_outlive_it()
	{
		await InstallVersion("1.0.0");
		_supervisor.OnStop = (pluginId, reason) =>
		{
			if (reason == PluginStopReason.Update)
			{
				_takeovers.Begin(pluginId);
			}
		};
		var startsBefore = _supervisor.Starts.Count;

		var update = await InstallVersion("2.0.0");

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.True, update.ErrorMessage);
			Assert.That(_takeovers.IsActive(PluginId), Is.False);
			Assert.That(_supervisor.Starts, Has.Count.GreaterThan(startsBefore), "the updated plugin resumes");
		});
	}

	private async Task BeginTakeover()
	{
		_takeovers.Begin(PluginId);
		using var scope = _provider.CreateScope();
		var registered = await scope.ServiceProvider.GetRequiredService<IPluginRegistrationService>()
			.Register(PluginId, "Development Build", null, PluginRegistrationOrigins.Pairing, allowInstalledId: true);
		Assert.That(registered.Succeeded, Is.True, "precondition: the takeover credential exists");
	}

	private async Task<PluginInstallResult> InstallVersion(string version, bool expectSuccess = true)
	{
		var artifact = new PluginArtifactBuilder()
			.WithManifest(ManifestJson.Build(version, PluginId, extraBlocks: null))
			.WithFile(ManifestJson.EntrypointExecutable, "binary")
			.WriteTo(_sourceDirectory, $"plugin-{version}.macroDeckPlugin");

		var result = await _installer.Install(PluginArtifactSource.FromPath(artifact), new PluginInstallRequest());
		if (expectSuccess)
		{
			Assert.That(result.Success, Is.True, $"precondition: installing {version}: {result.ErrorMessage}");
		}

		return result;
	}

	private string? ActiveVersion()
	{
		_catalog.Invalidate();
		return _catalog.TryResolveActive(PluginId, out var active) ? active!.Version : null;
	}

	private sealed class NoHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name)
			=> throw new InvalidOperationException("A unit test must not reach the network.");
	}
}
