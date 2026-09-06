using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Persistence;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationRegistryRegistrationTests
{
	private static readonly ILogger _logger = Log.Logger;

	private IntegrationRegistry _registry = null!;

	[SetUp]
	public void SetUp()
	{
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		_registry = new IntegrationRegistry(scopeFactory, new FakeIntegrationStateStore(), _logger);
	}

	[Test]
	public async Task A_valid_integration_registers_and_appears_in_Integrations()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene")]
		};

		var result = await _registry.RegisterAsync(integration);

		Assert.Multiple(() =>
		{
			Assert.That(result.Registered, Is.True);
			Assert.That(_registry.Integrations, Does.Contain(integration));
		});
	}

	[Test]
	public async Task A_duplicate_integration_id_is_rejected_and_the_first_registration_wins()
	{
		var first = new FakeIntegration { Id = "app.macro-deck.obs", Name = "First" };
		var second = new FakeIntegration { Id = "app.macro-deck.obs", Name = "Second" };

		await _registry.RegisterAsync(first);
		var result = await _registry.RegisterAsync(second);

		Assert.Multiple(() =>
		{
			Assert.That(result.Registered, Is.False);
			Assert.That(result.Failure, Is.EqualTo(IntegrationRegistrationFailure.DuplicateIntegrationId));
			Assert.That(_registry.Integrations, Does.Contain(first));
			Assert.That(_registry.Integrations, Does.Not.Contain(second));
		});
	}

	[Test]
	public async Task An_integration_with_a_duplicate_action_id_is_rejected_and_never_registered()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene"), new StubAction("set-scene")]
		};

		var result = await _registry.RegisterAsync(integration);

		Assert.Multiple(() =>
		{
			Assert.That(result.Registered, Is.False);
			Assert.That(result.Failure, Is.EqualTo(IntegrationRegistrationFailure.InvalidCapabilityIds));
			Assert.That(_registry.Integrations, Does.Not.Contain(integration));
			Assert.That(result.Describe(), Does.Contain("set-scene"));
		});
	}

	[Test]
	public async Task FindAction_by_qualified_id_resolves_the_same_action_as_by_pair()
	{
		var action = new StubAction("set-scene");
		var integration = new FakeIntegration { Id = "app.macro-deck.obs", Actions = [action] };
		await _registry.RegisterAsync(integration);

		var byPair = _registry.FindAction("app.macro-deck.obs", "set-scene");
		var byQualifiedId = _registry.FindAction(QualifiedId.Create("app.macro-deck.obs", "set-scene"));

		Assert.Multiple(() =>
		{
			Assert.That(byQualifiedId, Is.SameAs(action));
			Assert.That(byQualifiedId, Is.SameAs(byPair));
		});
	}

	[Test]
	public async Task GetActions_returns_a_descriptor_qualified_as_integration_and_action()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene")]
		};
		await _registry.RegisterAsync(integration);

		var descriptor = _registry.GetActions().Single();

		Assert.That(descriptor.Id.ToString(), Is.EqualTo("app.macro-deck.obs::set-scene"));
	}

	[Test]
	public async Task GetActions_excludes_an_action_restricted_to_other_platforms()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("hibernate", otherPlatforms), new StubAction("set-scene")]
		};
		await _registry.RegisterAsync(integration);

		var descriptors = _registry.GetActions();

		Assert.That(descriptors.Select(d => d.Id.LocalId), Is.EquivalentTo(["set-scene"]));
	}

	[Test]
	public async Task FindAction_does_not_return_an_action_restricted_to_other_platforms()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("hibernate", otherPlatforms)]
		};
		await _registry.RegisterAsync(integration);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.FindAction("app.macro-deck.obs", "hibernate"), Is.Null);
			Assert.That(_registry.FindAction(QualifiedId.Create("app.macro-deck.obs", "hibernate")), Is.Null);
		});
	}

	[Test]
	public async Task FindAction_returns_an_action_named_for_this_platform()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene", MacroDeckIntegrationAttribute.Current)]
		};
		await _registry.RegisterAsync(integration);

		Assert.That(_registry.FindAction("app.macro-deck.obs", "set-scene"), Is.Not.Null);
	}

	[Test]
	public async Task Unregistering_an_unknown_integration_reports_false()
	{
		var result = await _registry.UnregisterAsync("app.macro-deck.never-registered");

		Assert.That(result, Is.False);
	}

	[Test]
	public async Task Unregistering_a_registered_integration_removes_it_and_reports_true()
	{
		var integration = new FakeIntegration { Id = "app.macro-deck.obs" };
		await _registry.RegisterAsync(integration);

		var result = await _registry.UnregisterAsync(integration.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.True);
			Assert.That(_registry.Integrations, Does.Not.Contain(integration));
		});
	}

	[Test]
	public async Task Unregistering_an_integration_preserves_its_enabled_state()
	{
		var integration = new FakeIntegration { Id = "app.macro-deck.obs" };
		await _registry.RegisterAsync(integration);
		_registry.SetEnabled(integration.Id, false);

		await _registry.UnregisterAsync(integration.Id);
		await _registry.RegisterAsync(new FakeIntegration { Id = integration.Id });

		Assert.That(_registry.IsEnabled(integration.Id), Is.False);
	}

	[Test]
	public async Task Reregistering_the_same_plugin_id_replaces_the_existing_adapter()
	{
		var first = new FakeIntegration { Id = "com.example.plugin", Name = "First" };
		var second = new FakeIntegration { Id = "com.example.plugin", Name = "Second" };

		await _registry.RegisterAsync(first, IntegrationOrigin.Plugin);
		var result = await _registry.RegisterAsync(second, IntegrationOrigin.Plugin);

		Assert.Multiple(() =>
		{
			Assert.That(result.Registered, Is.True);
			Assert.That(_registry.Integrations, Does.Not.Contain(first));
			Assert.That(_registry.Integrations, Does.Contain(second));
		});
	}

	[Test]
	public async Task Reregistering_the_same_plugin_id_preserves_its_enabled_state()
	{
		var first = new FakeIntegration { Id = "com.example.plugin" };
		await _registry.RegisterAsync(first, IntegrationOrigin.Plugin);
		_registry.SetEnabled(first.Id, false);

		var second = new FakeIntegration { Id = "com.example.plugin" };
		await _registry.RegisterAsync(second, IntegrationOrigin.Plugin);

		Assert.That(_registry.IsEnabled(second.Id), Is.False);
	}

	[Test]
	public async Task A_plugin_id_colliding_with_a_built_in_integration_is_still_rejected()
	{
		var builtIn = new FakeIntegration { Id = "app.macro-deck.obs", Name = "Built-in" };
		var plugin = new FakeIntegration { Id = "app.macro-deck.obs", Name = "Plugin" };

		await _registry.RegisterAsync(builtIn);
		var result = await _registry.RegisterAsync(plugin, IntegrationOrigin.Plugin);

		Assert.Multiple(() =>
		{
			Assert.That(result.Registered, Is.False);
			Assert.That(result.Failure, Is.EqualTo(IntegrationRegistrationFailure.DuplicateIntegrationId));
			Assert.That(_registry.Integrations, Does.Contain(builtIn));
			Assert.That(_registry.Integrations, Does.Not.Contain(plugin));
		});
	}

	private sealed class FakeIntegrationStateStore : IIntegrationStateStore
	{
		private Dictionary<string, bool> _states = new();

		public IReadOnlyDictionary<string, bool> Load() => _states;

		public void Save(IReadOnlyDictionary<string, bool> states) => _states = new Dictionary<string, bool>(states);
	}

	private sealed class FakeIntegration : IIntegration
	{
		public string Id { get; init; } = "app.macro-deck.test";
		public LocalizedText Name { get; init; } = "Test";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions { get; init; } = [];
		public bool IsInitialized => true;

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class StubAction : IActionDefinition
	{
		public StubAction(string id, MacroDeckPlatform platforms = MacroDeckPlatform.All)
		{
			Id = id;
			Platforms = platforms;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters => [];
		public MacroDeckPlatform Platforms { get; }

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}
}
