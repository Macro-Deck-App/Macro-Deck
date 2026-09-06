using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Infrastructure.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventRegistryTests
{
	private static EventRegistry Registry(
		IIntegrationRegistry integrations,
		params IHostEventProvider[] hostProviders)
		=> new(integrations, hostProviders, new LoggerConfiguration().CreateLogger());

	private static IEnumerable<IHostEventProvider> HostProviders()
	{
		yield return new CoreEventProvider();
		yield return new MusicPlayerEventProvider(new RecordingEventBus());
		yield return new TimeEventProvider();
	}

	[TestCaseSource(nameof(HostProviders))]
	public void Every_host_provider_definition_carries_localized_references_only(IHostEventProvider provider)
	{
		Assert.That(provider.ProviderName.IsLocalized, Is.True, $"{provider.ProviderId} provider name");

		var definitionCount = 0;

		Assert.Multiple(() =>
		{
			foreach (var definition in provider.EventDefinitions)
			{
				definitionCount++;

				Assert.That(definition.Name.IsLocalized, Is.True, $"{definition.Id} name");

				if (!definition.Description.IsEmpty)
				{
					Assert.That(definition.Description.IsLocalized, Is.True, $"{definition.Id} description");
				}

				if (!definition.Category.IsEmpty)
				{
					Assert.That(definition.Category.IsLocalized, Is.True, $"{definition.Id} category");
				}

				foreach (var parameter in definition.ConfigurationParameters.Concat(definition.PayloadParameters))
				{
					AssertParameterIsLocalized(definition.Id, parameter);
				}
			}
		});

		Assert.That(definitionCount, Is.GreaterThan(0), $"{provider.ProviderId} declared no events");
	}

	private static void AssertParameterIsLocalized(string eventId, ActionParameter parameter)
	{
		if (!parameter.Label.IsEmpty)
		{
			Assert.That(parameter.Label.IsLocalized, Is.True, $"{eventId} parameter {parameter.Name} label");
		}

		if (!parameter.Description.IsEmpty)
		{
			Assert.That(parameter.Description.IsLocalized,
				Is.True,
				$"{eventId} parameter {parameter.Name} description");
		}

		if (parameter.Options is null)
		{
			return;
		}

		foreach (var option in parameter.Options)
		{
			Assert.That(option.Label.IsLocalized,
				Is.True,
				$"{eventId} parameter {parameter.Name} option {option.Value}");
		}
	}

	[Test]
	public void Host_provider_definitions_are_namespaced_by_provider_id()
	{
		var registry = Registry(new FakeIntegrationRegistry(), new CoreEventProvider());

		var definitions = registry.GetDefinitions();

		Assert.Multiple(() =>
		{
			Assert.That(definitions.Select(d => d.Id.ToString()), Does.Contain("macro-deck::variable-changed"));
			Assert.That(definitions.All(d => TestLocalization.Resolve(d.ProviderName) == "Macro Deck"), Is.True);
			Assert.That(definitions.All(d => !d.IsIntegrationProvider), Is.True);
		});
	}

	[Test]
	public void Integration_provider_definitions_are_namespaced_by_integration_id()
	{
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new EventProvidingIntegration());

		var definitions = Registry(integrations).GetDefinitions();

		Assert.Multiple(() =>
		{
			Assert.That(definitions, Has.Count.EqualTo(1));
			Assert.That(definitions[0].Id.ToString(), Is.EqualTo("app.macro-deck.fake::scene-changed"));
			Assert.That(TestLocalization.Resolve(definitions[0].ProviderName), Is.EqualTo("Fake Provider"));
			Assert.That(definitions[0].IsIntegrationProvider, Is.True);
		});
	}

	[Test]
	public void A_provider_that_states_no_name_is_described_by_its_integration_s_name()
	{
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new UnnamedEventProvidingIntegration());
		var registry = Registry(integrations);

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(registry.GetDefinitions()[0].ProviderName),
				Is.EqualTo("Unnamed Provider Integration"));
			Assert.That(TestLocalization.Resolve(registry.Find("app.macro-deck.unnamed::scene-changed")?.ProviderName),
				Is.EqualTo("Unnamed Provider Integration"));
		});
	}

	[Test]
	public void An_integration_that_provides_no_events_contributes_nothing()
	{
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new FakeIntegration { Id = "plain" });

		Assert.That(Registry(integrations).GetDefinitions(), Is.Empty);
	}

	[Test]
	public void A_disabled_integration_contributes_nothing()
	{
		var integrations = new DisabledIntegrationRegistry();
		integrations.Add(new EventProvidingIntegration());

		Assert.Multiple(() =>
		{
			Assert.That(Registry(integrations).GetDefinitions(), Is.Empty);
			Assert.That(Registry(integrations).Find("app.macro-deck.fake::scene-changed"), Is.Null);
		});
	}

	[Test]
	public void Find_resolves_a_qualified_id_from_either_kind_of_provider()
	{
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new EventProvidingIntegration());
		var registry = Registry(integrations, new CoreEventProvider());

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(registry.Find("app.macro-deck.fake::scene-changed")?.Definition.Name),
				Is.EqualTo("Scene Changed"));
			Assert.That(TestLocalization.Resolve(registry.Find("macro-deck::variable-changed")?.Definition.Name),
				Is.EqualTo("Variable Changed"));
		});
	}

	[Test]
	public void Find_returns_null_for_an_unqualified_or_unknown_id()
	{
		var registry = Registry(new FakeIntegrationRegistry(), new CoreEventProvider());

		Assert.Multiple(() =>
		{
			Assert.That(registry.Find("variable-changed"), Is.Null);
			Assert.That(registry.Find("macro-deck::nope"), Is.Null);
			Assert.That(registry.Find("nope::variable-changed"), Is.Null);
			Assert.That(registry.Find("::"), Is.Null);
		});
	}

	[Test]
	public void The_variable_changed_event_requires_a_variable()
	{
		var definition = new CoreEventProvider().EventDefinitions
			.First(d => d.Id == EventIds.VariableChanged);

		var variable = definition.ConfigurationParameters.First(p => p.Name == "variable");

		Assert.Multiple(() =>
		{
			Assert.That(variable.Required, Is.True);
			Assert.That(variable.Type, Is.EqualTo(ActionParameterType.Autocomplete));
			Assert.That(variable.OptionsSourceId, Is.EqualTo("macrodeck.variables"));
		});
	}

	[Test]
	public void The_folder_changed_event_reports_the_device_by_its_registered_identity()
	{
		var definition = new CoreEventProvider().EventDefinitions
			.First(d => d.Id == EventIds.FolderChanged);

		var device = definition.PayloadParameters.First(p => p.Name == "deviceId");

		Assert.Multiple(() =>
		{
			Assert.That(device.Type, Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(device.OptionsSourceId, Is.EqualTo("macrodeck.devices"));
		});
	}

	[TestCaseSource(nameof(HostProviders))]
	public void A_payload_parameter_offering_options_can_actually_resolve_them(IHostEventProvider provider)
	{
		// A payload picker with nothing behind it renders empty forever, and a condition authored
		// against it can never be filled in.
		var hasProvider = provider is IDynamicEventOptionsProvider;

		Assert.Multiple(() =>
		{
			foreach (var definition in provider.EventDefinitions)
			{
				foreach (var parameter in definition.PayloadParameters.Where(OffersOptions))
				{
					Assert.That(parameter.Options is { Count: > 0 } ||
						parameter.OptionsSourceId is not null ||
						hasProvider,
						Is.True,
						$"{definition.Id}/{parameter.Name}");
				}
			}
		});
	}

	private static bool OffersOptions(ActionParameter parameter)
		=> parameter.Type is ActionParameterType.Choice
			or ActionParameterType.DynamicChoice
			or ActionParameterType.Autocomplete;

	private sealed class EventProvidingIntegration : IIntegration, IEventProvider
	{
		public string Id => "app.macro-deck.fake";
		public LocalizedText Name => "Fake";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public string ProviderName => "Fake Provider";

		public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
			[new() { Id = "scene-changed", Name = "Scene Changed" }];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class UnnamedEventProvidingIntegration : IIntegration, IEventProvider
	{
		public string Id => "app.macro-deck.unnamed";
		public LocalizedText Name => "Unnamed Provider Integration";
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;

		public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
			[new() { Id = "scene-changed", Name = "Scene Changed" }];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class DisabledIntegrationRegistry : IIntegrationRegistry
	{
		public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged
		{
			add { }
			remove { }
		}

		private readonly List<IIntegration> _integrations = [];

		public IReadOnlyList<IIntegration> Integrations => _integrations;

		public void Add(IIntegration integration) => _integrations.Add(integration);

		public IActionDefinition? FindAction(string integrationId, string actionId) => null;

		public IActionDefinition? FindAction(QualifiedId id) => null;

		public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

		public bool IsEnabled(string integrationId) => false;

		public void SetEnabled(string integrationId, bool enabled)
		{
		}

		public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

		public Task<IntegrationRegistrationResult> RegisterAsync(
			IIntegration integration,
			IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
			IntegrationMetadata? metadata = null)
			=> Task.FromResult(IntegrationRegistrationResult.Success);

		public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
	}
}
