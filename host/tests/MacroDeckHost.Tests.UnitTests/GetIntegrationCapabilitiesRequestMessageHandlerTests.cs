using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using DomainEnums = MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests;

public class GetIntegrationCapabilitiesRequestMessageHandlerTests
{
	private static StartupReadiness Ready()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private static GetIntegrationCapabilitiesRequestMessageHandler CreateHandler(
		ConfigurableIntegrationRegistry registry,
		FakeIntegrationConfigStore configStore,
		VariableRegistry variableRegistry,
		StartupReadiness? readiness = null)
		=> new(registry,
			configStore,
			variableRegistry,
			readiness ?? Ready(),
			TestLocalization.Preferences,
			TestLocalization.Resolver);

	[Test]
	public async Task Unconfigured_lists_both_catalogs_as_SetupRequired()
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int1",
			IsInitialized = false,
			Actions = [new CapturingActionDefinition()],
			Variables = [],
			DeclaredVariablesOverride = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration], disabled: [integration.Id]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 0);
		var handler = CreateHandler(registry, configStore, new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Found, Is.True);
			Assert.That(response.RequiresSetup, Is.True);
			Assert.That(response.Actions.Single().Availability, Is.EqualTo(CapabilityAvailability.SetupRequired));
			Assert.That(response.Variables.Single().Availability, Is.EqualTo(CapabilityAvailability.SetupRequired));
		});
	}

	[Test]
	public async Task Configured_but_disabled_reports_IntegrationDisabled()
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int2",
			IsInitialized = false,
			Actions = [new CapturingActionDefinition()],
			Variables = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration], disabled: [integration.Id]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 1);
		var handler = CreateHandler(registry, configStore, new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Enabled, Is.False);
			Assert.That(response.RequiresSetup, Is.False);
			Assert.That(response.Actions.Single().Availability, Is.EqualTo(CapabilityAvailability.IntegrationDisabled));
			Assert.That(response.Variables.Single().Availability,
				Is.EqualTo(CapabilityAvailability.IntegrationDisabled));
		});
	}

	[Test]
	public async Task Enabled_but_not_initialized_reports_Unavailable()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int3",
			IsInitialized = false,
			Actions = [new CapturingActionDefinition()],
			Variables = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Enabled, Is.True);
			Assert.That(response.IsInitialized, Is.False);
			Assert.That(response.Actions.Single().Availability, Is.EqualTo(CapabilityAvailability.Unavailable));
			Assert.That(response.Variables.Single().Availability, Is.EqualTo(CapabilityAvailability.Unavailable));
		});
	}

	[Test]
	public async Task Active_integration_reports_Ready_with_live_values()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int4",
			IsInitialized = true,
			Actions = [new CapturingActionDefinition()],
			Variables = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric, decimalPlaces: 1)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		variableRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "cpu_usage",
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Numeric,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = integration.Id,
			Value = "42.5",
			DecimalPlaces = 1,
			UpdatedAt = DateTime.UtcNow
		});
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(response.Actions.Single().Availability, Is.EqualTo(CapabilityAvailability.Ready));
			Assert.That(variable.Availability, Is.EqualTo(CapabilityAvailability.Ready));
			Assert.That(variable.Type, Is.EqualTo("numeric"));
			Assert.That(variable.Value, Is.EqualTo("42.5"));
			Assert.That(variable.ValueAvailable, Is.True);
		});
	}

	[Test]
	public async Task An_action_restricted_to_other_platforms_is_absent_from_the_catalog()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int-restricted",
			IsInitialized = true,
			Actions =
			[
				new CapturingActionDefinition { Id = "hibernate", Platforms = otherPlatforms },
				new CapturingActionDefinition { Id = "sleep" }
			],
			Variables = []
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.That(response.Actions.Select(a => a.Id), Is.EquivalentTo(["sleep"]));
	}

	/// <summary>
	/// Group C scenarios 11+12, one layer further out from <c>ActionsContractTests</c>'s wire-level
	/// proof: <see cref="IntegrationActionCapability.IsIconProviderAction" /> and
	/// <see cref="IntegrationActionCapability.IsStateProviderAction" /> are independent flags computed
	/// from two different marker interfaces, not one mistakenly standing in for the other.
	/// </summary>
	[Test]
	public async Task IsIconProviderAction_and_IsStateProviderAction_are_independent()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int-icon-state",
			IsInitialized = true,
			Actions = [new IconOnlyActionDefinition(), new StateOnlyActionDefinition()],
			Variables = []
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var iconOnly = response.Actions.Single(a => a.Id == "icon-only");
		var stateOnly = response.Actions.Single(a => a.Id == "state-only");

		Assert.Multiple(() =>
		{
			Assert.That(iconOnly.IsIconProviderAction, Is.True);
			Assert.That(iconOnly.IsStateProviderAction, Is.False);
			Assert.That(stateOnly.IsIconProviderAction, Is.False);
			Assert.That(stateOnly.IsStateProviderAction, Is.True);
		});
	}

	[Test]
	public async Task Disconnected_variable_reports_Unavailable_with_the_count_unchanged()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int5",
			IsInitialized = true,
			Variables = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		var variableId = Guid.NewGuid();
		variableRegistry.Upsert(new VariableEntity
		{
			Id = variableId,
			Name = "cpu_usage",
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Numeric,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = integration.Id,
			Value = "42.5",
			UpdatedAt = DateTime.UtcNow
		});
		variableRegistry.SetAvailable(variableId, false);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Variables, Has.Count.EqualTo(1), "the variable is still catalogued");
			Assert.That(response.Variables.Single().Availability, Is.EqualTo(CapabilityAvailability.Unavailable));
		});
	}

	[Test]
	public async Task DeclaredVariables_is_used_when_ProvidedVariables_is_empty()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int6",
			IsInitialized = true,
			Variables = [],
			DeclaredVariablesOverride = [VariableDefinition.Eager("strip_0_gain", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Variables, Has.Count.EqualTo(1));
			Assert.That(response.Variables.Single().Name, Is.EqualTo("strip_0_gain"));

			Assert.That(response.Variables.Single().Availability, Is.EqualTo(CapabilityAvailability.Unavailable));
		});
	}

	[Test]
	public async Task Template_variables_are_flagged_and_never_looked_up_at_runtime()
	{
		const string templateName = "twitch_<account>_is_live";
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int7",
			IsInitialized = true,
			VariablesDependOnConfiguration = true,
			Variables = [],
			DeclaredVariablesOverride = [VariableDefinition.Eager(templateName, VariableType.Boolean)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var configStore = new FakeIntegrationConfigStore();
		var variableRegistry = new VariableRegistry();

		variableRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = templateName,
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Boolean,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = integration.Id,
			Value = "true",
			UpdatedAt = DateTime.UtcNow
		});

		var handler = CreateHandler(registry, configStore, variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(variable.IsTemplate, Is.True);
			Assert.That(variable.Availability, Is.EqualTo(CapabilityAvailability.AvailableAfterSetup));
			Assert.That(variable.Value, Is.Null, "a template is never resolved to a runtime value");
			Assert.That(variable.ValueAvailable, Is.False);
		});
	}

	[Test]
	public async Task A_configured_but_disabled_template_provider_reports_the_integration_as_disabled()
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int8",
			IsInitialized = false,
			VariablesDependOnConfiguration = true,
			Variables = [],
			DeclaredVariablesOverride = [VariableDefinition.Eager("twitch_<account>_is_live", VariableType.Boolean)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		registry.SetEnabled(integration.Id, false);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 1);

		var handler = CreateHandler(registry, configStore, new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(variable.Availability, Is.EqualTo(CapabilityAvailability.IntegrationDisabled));
			Assert.That(variable.AvailabilityReason, Is.EqualTo("Integration disabled"));
			Assert.That(variable.IsTemplate, Is.True, "it is still a template, only not the reason it is unusable");
		});
	}

	[Test]
	public async Task Browsing_initializes_nothing_and_creates_no_runtime_variables()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "int8",
			IsInitialized = false,
			Variables = []
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();

		// Never marked ready: since the integration is not initialized the handler must not wait on it,
		// so this also proves the browse path does not block on startup here.
		var readiness = new StartupReadiness();
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry, readiness);

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			cts.Token);

		Assert.Multiple(() =>
		{
			Assert.That(response.Found, Is.True);
			Assert.That(integration.InitializeCallCount, Is.EqualTo(0));
			Assert.That(integration.IsInitialized, Is.False);
			Assert.That(variableRegistry.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task Unknown_id_reports_Found_false()
	{
		var registry = new ConfigurableIntegrationRegistry([]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = "missing" },
			CancellationToken.None);

		Assert.That(response.Found, Is.False);
	}

	[Test]
	public async Task System_integrations_are_hidden()
	{
		var system = StubIntegration.Create("system-1", system: true);
		var registry = new ConfigurableIntegrationRegistry([system]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = "system-1" },
			CancellationToken.None);

		Assert.That(response.Found, Is.False);
	}

	private sealed class IconOnlyActionDefinition : IActionDefinition, IIconProviderActionDefinition
	{
		public string Id => "icon-only";

		public LocalizedText Name => "Icon Only";

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();

		public Task<ActionIconSnapshot?> GetActionIconAsync(IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult<ActionIconSnapshot?>(null);
	}

	private sealed class StateOnlyActionDefinition : IActionDefinition, IStateProviderActionDefinition
	{
		public string Id => "state-only";

		public LocalizedText Name => "State Only";

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();

		public Task<ActionStateSnapshot?> GetActionStateAsync(IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
			=> Task.FromResult<ActionStateSnapshot?>(null);
	}
}
