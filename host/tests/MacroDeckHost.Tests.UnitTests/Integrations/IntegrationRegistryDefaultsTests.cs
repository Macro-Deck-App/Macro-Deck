using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationRegistryDefaultsTests
{
	private static readonly ILogger _logger = Log.Logger;

	private FakeIntegrationStateStore _stateStore = null!;
	private IntegrationRegistry _registry = null!;

	[SetUp]
	public void SetUp()
	{
		_stateStore = new FakeIntegrationStateStore();
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		_registry = new IntegrationRegistry(scopeFactory, _stateStore, _logger);
	}

	[Test]
	public async Task An_integration_without_an_opt_out_is_enabled()
	{
		await _registry.RegisterAsync(new PlainIntegration());

		Assert.That(_registry.IsEnabled(PlainIntegration.IntegrationId), Is.True);
	}

	[Test]
	public async Task An_integration_that_opts_out_starts_disabled()
	{
		await _registry.RegisterAsync(new OptedOutIntegration());

		Assert.That(_registry.IsEnabled(OptedOutIntegration.IntegrationId), Is.False);
	}

	[Test]
	public async Task A_stored_choice_beats_the_opt_out()
	{
		await _registry.RegisterAsync(new OptedOutIntegration());
		_registry.SetEnabled(OptedOutIntegration.IntegrationId, true);

		var restarted = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			_stateStore,
			_logger);
		await restarted.RegisterAsync(new OptedOutIntegration());

		Assert.That(restarted.IsEnabled(OptedOutIntegration.IntegrationId), Is.True);
	}

	[Test]
	public async Task An_integration_with_a_config_flow_still_starts_disabled()
	{
		await _registry.RegisterAsync(new ConfiguredIntegration());

		Assert.That(_registry.IsEnabled(ConfiguredIntegration.IntegrationId), Is.False);
	}

	[Test]
	public async Task Origin_reflects_how_the_integration_was_registered()
	{
		await _registry.RegisterAsync(new PlainIntegration());
		await _registry.RegisterAsync(new RemotePluginIntegration(), IntegrationOrigin.Plugin);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.GetOrigin(PlainIntegration.IntegrationId), Is.EqualTo(IntegrationOrigin.BuiltIn));
			Assert.That(_registry.GetOrigin(RemotePluginIntegration.IntegrationId),
				Is.EqualTo(IntegrationOrigin.Plugin));
		});
	}

	[Test]
	public async Task A_plugin_with_a_config_flow_registers_disabled_by_default()
	{
		await _registry.RegisterAsync(new RemoteConfiguredPluginIntegration(),
			IntegrationOrigin.Plugin,
			new IntegrationMetadata { EnabledByDefault = true });

		Assert.That(_registry.IsEnabled(RemoteConfiguredPluginIntegration.IntegrationId), Is.False);
	}

	private sealed class FakeIntegrationStateStore : IIntegrationStateStore
	{
		private Dictionary<string, bool> _states = new();

		public IReadOnlyDictionary<string, bool> Load() => _states;

		public void Save(IReadOnlyDictionary<string, bool> states) => _states = new Dictionary<string, bool>(states);
	}

	[MacroDeckIntegration]
	private sealed class PlainIntegration : IIntegration
	{
		public const string IntegrationId = "test.plain";

		public string Id => IntegrationId;
		public LocalizedText Name => "Plain";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	[MacroDeckIntegration(EnabledByDefault = false)]
	private sealed class OptedOutIntegration : IIntegration
	{
		public const string IntegrationId = "test.opted-out";

		public string Id => IntegrationId;
		public LocalizedText Name => "Opted out";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	[MacroDeckIntegration]
	private sealed class ConfiguredIntegration : IIntegration, IConfigFlowProvider
	{
		public const string IntegrationId = "test.configured";

		public string Id => IntegrationId;
		public LocalizedText Name => "Configured";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
	}

	private sealed class RemotePluginIntegration : IIntegration
	{
		public const string IntegrationId = "test.remote-plugin";

		public string Id => IntegrationId;
		public LocalizedText Name => "Remote plugin";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class RemoteConfiguredPluginIntegration : IIntegration, IConfigFlowProvider
	{
		public const string IntegrationId = "test.remote-plugin-configured";

		public string Id => IntegrationId;
		public LocalizedText Name => "Remote configured plugin";
		public string Version => "1.0.0";
		public bool IsInitialized => true;
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public IConfigFlow CreateConfigFlow() => throw new NotSupportedException();
	}
}
