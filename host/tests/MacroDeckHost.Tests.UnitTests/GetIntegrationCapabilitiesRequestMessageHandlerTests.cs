using MacroDeckHost.Application.Integrations;
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
using NullUserVariableStore = MacroDeckHost.Tests.UnitTests.Variables.NullUserVariableStore;

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
	{
		return new GetIntegrationCapabilitiesRequestMessageHandler(registry,
			configStore,
			variableRegistry,
			readiness ?? Ready(),
			TestLocalization.Preferences,
			TestLocalization.Resolver);
	}

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
		var registry = new ConfigurableIntegrationRegistry([integration], [integration.Id]);
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
	public async Task An_optional_config_flow_without_entries_does_not_require_setup()
	{
		var integration = new FakeConfigurableVariableProviderIntegration
		{
			Id = "int-optional",
			IsInitialized = true,
			RequiresConfiguration = false,
			Actions = [new CapturingActionDefinition()],
			Variables = []
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, 0);
		var handler = CreateHandler(registry, configStore, new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.SupportsConfigFlow, Is.True);
			Assert.That(response.RequiresSetup, Is.False);
			Assert.That(response.Actions.Single().Availability, Is.EqualTo(CapabilityAvailability.Ready));
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
		var registry = new ConfigurableIntegrationRegistry([integration], [integration.Id]);
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
			Variables = [VariableDefinition.Eager("cpu_usage", VariableType.Numeric, 1)]
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

	[TestCase(false, CapabilityAvailability.Ready, "64")]
	[TestCase(true, CapabilityAvailability.IntegrationDisabled, null)]
	public async Task Battery_variables_the_host_wrote_for_an_integration_are_listed_with_it(
		bool disabled,
		CapabilityAvailability expected,
		string? expectedValue)
	{
		var integration = new FakeVariableProviderIntegration { Id = "com.example.headsets", IsInitialized = true };
		var registry = new ConfigurableIntegrationRegistry([integration], disabled ? [integration.Id] : []);
		var variableRegistry = new VariableRegistry();
		foreach (var (name, owner, value) in new[]
			{
				("battery_arctis_headset_d15c7416_level", integration.Id, "64"),
				("battery_pixel_8_3f9a1c2e_level", "app.macro-deck.adb", "80"),
				("headset_mode", integration.Id, "1")
			})
		{
			variableRegistry.Upsert(new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = name,
				Scope = DomainEnums.VariableScope.Global,
				Type = DomainEnums.VariableType.Numeric,
				Classification = DomainEnums.VariableClassification.Integration,
				OwnerIntegrationId = owner,
				Value = value,
				UpdateMode = DomainEnums.VariableUpdateMode.Pushed,
				PublishedByHost = name.StartsWith("battery_", StringComparison.Ordinal),
				UpdatedAt = DateTime.UtcNow
			});
		}

		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(variable.Name, Is.EqualTo("battery_arctis_headset_d15c7416_level"));
			Assert.That(variable.Availability, Is.EqualTo(expected));
			Assert.That(variable.Value, Is.EqualTo(expectedValue));
		});
	}

	[Test]
	public async Task A_variable_the_integration_wrote_itself_is_not_taken_for_a_host_published_one()
	{
		var integration = new FakeVariableProviderIntegration { Id = "com.example.headsets", IsInitialized = true };
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		variableRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "battery_arctis_headset_d15c7416_level",
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Numeric,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = integration.Id,
			Value = "64",
			UpdatedAt = DateTime.UtcNow
		});
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.That(response.Variables, Is.Empty);
	}

	[Test]
	public async Task The_overview_counts_the_same_variables_the_detail_lists()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "com.example.headsets",
			IsInitialized = true,
			Variables = [VariableDefinition.Eager("headset_mode", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		foreach (var name in new[]
			{ "headset_mode", "battery_arctis_headset_d15c7416_level", "battery_arctis_headset_d15c7416_charging" })
		{
			variableRegistry.Upsert(new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = name,
				Scope = DomainEnums.VariableScope.Global,
				Type = DomainEnums.VariableType.Numeric,
				Classification = DomainEnums.VariableClassification.Integration,
				OwnerIntegrationId = integration.Id,
				Value = "1",
				UpdateMode = DomainEnums.VariableUpdateMode.Pushed,
				PublishedByHost = name.StartsWith("battery_", StringComparison.Ordinal),
				UpdatedAt = DateTime.UtcNow
			});
		}

		var overview = new GetIntegrationsRequestMessageHandler(registry,
			new FakeIntegrationConfigStore(),
			new IntegrationIssueService(registry, new FakeIntegrationHostIssueStore(), new FakeIntegrationLifecycle()),
			variables: variableRegistry);
		var detail = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var listed = await overview.Handle(new GetIntegrationsRequest(), CancellationToken.None);
		var capabilities = await detail.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(capabilities.Variables, Has.Count.EqualTo(3));
			Assert.That(listed.Integrations.Single().VariableCount, Is.EqualTo(capabilities.Variables.Count));
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
	public async Task A_declared_name_the_host_sanitized_is_listed_under_its_published_name_with_its_value()
	{
		var declared = VariableDefinition.Eager("ts3-current-channel", VariableType.Text) with
		{
			Id = "ts3-current-channel"
		};
		var integration = new FakeVariableProviderIntegration
		{
			Id = "com.example.teamspeak", IsInitialized = true, Variables = [declared]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		var service = TestVariableServices.Create(variableRegistry,
			new NullUserVariableStore(),
			new RecordingMediator());
		var created = await service.CreateIntegrationVariable(integration.Id,
			declared.Name!,
			DomainEnums.VariableScope.Global,
			null,
			DomainEnums.VariableType.Text,
			null,
			null,
			declared.ResolvedId);
		await service.ReportIntegrationVariableValue(integration.Id, created.Data!.Id, "Lobby");
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(variable.Name, Is.EqualTo(created.Data.Name));
			Assert.That(variable.Name, Is.EqualTo("ts3_current_channel"));
			Assert.That(variable.Availability, Is.EqualTo(CapabilityAvailability.Ready));
			Assert.That(variable.Value, Is.EqualTo("Lobby"));
		});
	}

	[TestCase(true, CapabilityAvailability.IntegrationDisabled)]
	[TestCase(false, CapabilityAvailability.Unavailable)]
	public async Task A_sanitized_declared_name_keeps_its_published_name_while_it_cannot_be_read(
		bool disabled,
		CapabilityAvailability expected)
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "com.example.teamspeak",
			IsInitialized = false,
			Variables = [VariableDefinition.Eager("ts3-input-state", VariableType.Boolean)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration], disabled ? [integration.Id] : []);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		var variable = response.Variables.Single();
		Assert.Multiple(() =>
		{
			Assert.That(variable.Name, Is.EqualTo("ts3_input_state"));
			Assert.That(variable.Availability, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task A_valid_declared_name_is_listed_unchanged()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "com.example.sensors",
			IsInitialized = true,
			Variables = [VariableDefinition.Eager("cpu__temp", VariableType.Numeric)]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry, new FakeIntegrationConfigStore(), new VariableRegistry());

		var response = await handler.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.That(response.Variables.Single().Name, Is.EqualTo("cpu__temp"));
	}

	[Test]
	public async Task Declared_names_that_publish_as_one_variable_are_listed_and_counted_once()
	{
		var integration = new FakeVariableProviderIntegration
		{
			Id = "com.example.headsets",
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("headset-mode", VariableType.Numeric) with { Id = "headset-mode" },
				VariableDefinition.Eager("headset_mode", VariableType.Numeric) with { Id = "headset-mode-2" }
			]
		};
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var variableRegistry = new VariableRegistry();
		variableRegistry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "battery_arctis_headset_d15c7416_level",
			Scope = DomainEnums.VariableScope.Global,
			Type = DomainEnums.VariableType.Numeric,
			Classification = DomainEnums.VariableClassification.Integration,
			OwnerIntegrationId = integration.Id,
			Value = "64",
			UpdateMode = DomainEnums.VariableUpdateMode.Pushed,
			PublishedByHost = true,
			UpdatedAt = DateTime.UtcNow
		});
		var overview = new GetIntegrationsRequestMessageHandler(registry,
			new FakeIntegrationConfigStore(),
			new IntegrationIssueService(registry, new FakeIntegrationHostIssueStore(), new FakeIntegrationLifecycle()),
			variables: variableRegistry);
		var detail = CreateHandler(registry, new FakeIntegrationConfigStore(), variableRegistry);

		var listed = await overview.Handle(new GetIntegrationsRequest(), CancellationToken.None);
		var capabilities = await detail.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = integration.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(capabilities.Variables.Select(v => v.Name),
				Is.EqualTo(new[] { "headset_mode", "battery_arctis_headset_d15c7416_level" }));
			Assert.That(listed.Integrations.Single().VariableCount, Is.EqualTo(capabilities.Variables.Count));
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

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<ActionIconSnapshot?> GetActionIconAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<ActionIconSnapshot?>(null);
		}
	}

	private sealed class StateOnlyActionDefinition : IActionDefinition, IStateProviderActionDefinition
	{
		public string Id => "state-only";

		public LocalizedText Name => "State Only";

		public LocalizedText Description => string.Empty;

		public IReadOnlyList<ActionParameter> Parameters { get; } = [];

		public IActionExecutor CreateExecutor()
		{
			throw new NotSupportedException();
		}

		public Task<ActionStateSnapshot?> GetActionStateAsync(
			IReadOnlyDictionary<string, object?> parameters,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}
	}
}
