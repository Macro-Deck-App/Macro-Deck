using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;

namespace MacroDeckHost.Tests.UnitTests;

public class IntegrationOverviewCapabilitiesTests
{
	private static readonly string[] _actionsVariablesMusicPlayerNames = ["Actions", "Variables", "Music Player"];

	private static readonly string[] _actionsMusicPlayerNames = ["Actions", "Music Player"];

	private static GetIntegrationsRequestMessageHandler CreateHandler(ConfigurableIntegrationRegistry registry)
		=> new(registry,
			new FakeIntegrationConfigStore(),
			new IntegrationIssueService(registry, new FakeIntegrationHostIssueStore(), new FakeIntegrationLifecycle()));

	private static async Task<Integration> HandleSingle(IIntegration integration)
	{
		var registry = new ConfigurableIntegrationRegistry([integration]);
		var handler = CreateHandler(registry);
		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);
		return response.Integrations.Single();
	}

	[TestCase("events", CapabilityKinds.Events, "Events")]
	[TestCase("variables", CapabilityKinds.Variables, "Variables")]
	[TestCase("music-player", CapabilityKinds.MusicPlayer, "Music Player")]
	[TestCase("weather", CapabilityKinds.Weather, "Weather")]
	[TestCase("virtual-profiles", CapabilityKinds.VirtualProfiles, "Virtual Profiles")]
	[TestCase("migration", CapabilityKinds.Migration, "Migration")]
	public async Task Each_provider_interface_is_reported_as_its_own_human_readable_capability(
		string soleProvider,
		string expectedKind,
		string expectedName)
	{
		IIntegration integration = soleProvider switch
		{
			"events" => new FakeEventOnlyIntegration(),
			"variables" => new FakeVariableProviderIntegration(),
			"music-player" => new FakeMusicPlayerOnlyIntegration(),
			"weather" => new FakeWeatherOnlyIntegration(),
			"virtual-profiles" => new FakeProfileProviderIntegration("fake-profile-provider-solo"),
			"migration" => new FakeMigrationOnlyIntegration(),
			_ => throw new ArgumentOutOfRangeException(nameof(soleProvider))
		};

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities, Has.Count.EqualTo(1));
		Assert.That(dto.ProvidedCapabilities[0].Kind, Is.EqualTo(expectedKind));
		Assert.That(TestLocalization.Resolve(dto.ProvidedCapabilities[0].Name), Is.EqualTo(expectedName));
	}

	[Test]
	public async Task Internal_capability_interfaces_never_become_visible_capabilities()
	{
		var integration = new FakeHiddenCapabilitiesIntegration();

		var dto = await HandleSingle(integration);

		Assert.Multiple(() =>
		{
			var kinds = dto.ProvidedCapabilities.Select(c => c.Kind).ToHashSet(StringComparer.Ordinal);
			Assert.That(kinds, Is.EquivalentTo(new[] { CapabilityKinds.Events, CapabilityKinds.MusicPlayer }));

			// The exclusion must not be implemented by breaking these - they read from the same casts.
			Assert.That(dto.HasIcon, Is.True);
			Assert.That(dto.SupportsConfigFlow, Is.True);
			Assert.That(dto.IssueCount, Is.EqualTo(1));
		});
	}

	[TestCase(false, 0, false, TestName = "Not_configured")]
	[TestCase(false, 1, false, TestName = "Disabled")]
	[TestCase(true, 1, false, TestName = "Enabled_not_initialized")]
	[TestCase(true, 1, true, TestName = "Enabled_initialized_and_configured")]
	public async Task Capabilities_do_not_depend_on_enabled_initialized_or_configured_state(
		bool enabled,
		int configuredEntryCount,
		bool isInitialized)
	{
		var integration = new FakeConfigurableMusicPlayerIntegration { IsInitialized = isInitialized };
		var registry = new ConfigurableIntegrationRegistry([integration], enabled ? [] : [integration.Id]);
		var configStore = new FakeIntegrationConfigStore();
		configStore.SetConfiguredCount(integration.Id, configuredEntryCount);
		var handler = new GetIntegrationsRequestMessageHandler(registry,
			configStore,
			new IntegrationIssueService(registry, new FakeIntegrationHostIssueStore(), new FakeIntegrationLifecycle()));

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		var dto = response.Integrations.Single();
		Assert.That(dto.ProvidedCapabilities.Select(c => TestLocalization.Resolve(c.Name)),
			Is.EqualTo(_actionsVariablesMusicPlayerNames));
	}

	[Test]
	public async Task Provider_catalogues_are_never_read_while_building_the_overview()
	{
		var integration = new FakeThrowingProviderCatalogsIntegration();

		var dto = await HandleSingle(integration);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ProvidedCapabilities.Select(c => c.Kind),
				Is.EquivalentTo(new[]
				{
					CapabilityKinds.Events, CapabilityKinds.Variables, CapabilityKinds.MusicPlayer,
					CapabilityKinds.Weather, CapabilityKinds.VirtualProfiles
				}));
			Assert.That(integration.InitializeCallCount, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task An_integration_whose_actions_can_drive_button_states_advertises_that_capability()
	{
		var integration = new FakeStateProviderActionIntegration();

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities.Select(c => (c.Kind, TestLocalization.Resolve(c.Name))),
			Does.Contain((ProvidedCapabilityCatalog.ActionStatesKind, "Button States")));
	}

	[Test]
	public async Task An_integration_whose_actions_only_run_does_not_advertise_button_states()
	{
		var integration = new FakeConfigurableMusicPlayerIntegration();

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities.Select(c => c.Kind),
			Does.Not.Contain(ProvidedCapabilityCatalog.ActionStatesKind));
	}

	[Test]
	public async Task An_integration_whose_actions_can_provide_a_widget_icon_advertises_that_capability()
	{
		var integration = new FakeIconProviderActionIntegration();

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities.Select(c => (c.Kind, TestLocalization.Resolve(c.Name))),
			Does.Contain((ProvidedCapabilityCatalog.ActionIconsKind, "Button Icons")));
	}

	[Test]
	public async Task An_integration_whose_actions_only_run_does_not_advertise_action_icons()
	{
		var integration = new FakeConfigurableMusicPlayerIntegration();

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities.Select(c => c.Kind),
			Does.Not.Contain(ProvidedCapabilityCatalog.ActionIconsKind));
	}

	[Test]
	public async Task A_remote_plugin_advertises_button_states_from_its_action_descriptors()
	{
		var providing = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions],
			Actions = [ActionDescriptor() with { ProvidesState = true }]
		};
		var plain = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions], Actions = [ActionDescriptor()]
		};

		var withStates = await HandleSingle(CreateRemoteIntegration(providing));
		var withoutStates = await HandleSingle(CreateRemoteIntegration(plain));

		Assert.Multiple(() =>
		{
			Assert.That(withStates.ProvidedCapabilities.Select(c => c.Kind),
				Does.Contain(ProvidedCapabilityCatalog.ActionStatesKind));
			Assert.That(withoutStates.ProvidedCapabilities.Select(c => c.Kind),
				Does.Not.Contain(ProvidedCapabilityCatalog.ActionStatesKind));
		});
	}

	[Test]
	public async Task A_remote_plugin_advertises_action_icons_from_its_action_descriptors()
	{
		var providing = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions],
			Actions = [ActionDescriptor() with { ProvidesIcon = true }]
		};
		var plain = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions], Actions = [ActionDescriptor()]
		};

		var withIcons = await HandleSingle(CreateRemoteIntegration(providing));
		var withoutIcons = await HandleSingle(CreateRemoteIntegration(plain));

		Assert.Multiple(() =>
		{
			Assert.That(withIcons.ProvidedCapabilities.Select(c => c.Kind),
				Does.Contain(ProvidedCapabilityCatalog.ActionIconsKind));
			Assert.That(withoutIcons.ProvidedCapabilities.Select(c => c.Kind),
				Does.Not.Contain(ProvidedCapabilityCatalog.ActionIconsKind));
		});
	}

	private static RemotePluginIntegration CreateRemoteIntegration(
		RemotePluginCapabilitySnapshot snapshot,
		bool hasIcon = false,
		bool hasConfigFlow = false,
		bool hasDynamicEventOptions = false)
		=> RemotePluginIntegrationFactory.Create("com.example.plugin",
			"Example Plugin",
			"1.0.0",
			snapshot,
			new ThrowingInvoker(),
			new AlwaysDisconnected(),
			new InMemoryPluginAssetCache(),
			hasIcon,
			hasConfigFlow,
			hasDynamicEventOptions);

	private static RemoteActionDescriptor ActionDescriptor(string localId = "play")
		=> new(localId, "Play", string.Empty, [], null, false, false);

	[Test]
	public async Task A_remote_plugin_reports_only_the_kinds_it_negotiated()
	{
		var snapshot = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions], Actions = [ActionDescriptor()]
		};
		var integration = CreateRemoteIntegration(snapshot);

		var dto = await HandleSingle(integration);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ProvidedCapabilities, Has.Count.EqualTo(1));
			Assert.That(dto.ProvidedCapabilities[0].Kind, Is.EqualTo(CapabilityKinds.Actions));
			Assert.That(TestLocalization.Resolve(dto.ProvidedCapabilities[0].Name), Is.EqualTo("Actions"));

			var kinds = dto.ProvidedCapabilities.Select(c => c.Kind).ToList();
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.Events));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.Variables));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.MusicPlayer));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.Weather));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.VirtualProfiles));
		});
	}

	[Test]
	public async Task A_remote_plugin_with_no_negotiated_kinds_reports_none()
	{
		var snapshot = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			Actions = [ActionDescriptor()]
		};
		var integration = CreateRemoteIntegration(snapshot);

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities, Is.Empty);
	}

	[Test]
	public async Task A_remote_plugin_that_negotiated_every_kind_reports_the_full_visible_set()
	{
		var snapshot = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = CapabilityKinds.All
		};
		var integration
			= CreateRemoteIntegration(snapshot, hasIcon: true, hasConfigFlow: true, hasDynamicEventOptions: true);

		var dto = await HandleSingle(integration);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ProvidedCapabilities.Select(c => (c.Kind, TestLocalization.Resolve(c.Name))),
				Is.EqualTo(new[]
				{
					(CapabilityKinds.Actions, "Actions"),
					(CapabilityKinds.Events, "Events"),
					(CapabilityKinds.Variables, "Variables"),
					(CapabilityKinds.MusicPlayer, "Music Player"),
					(CapabilityKinds.Weather, "Weather"),
					(CapabilityKinds.VirtualProfiles, "Virtual Profiles"),
					(CapabilityKinds.DeviceProvider, "Devices"),
					(CapabilityKinds.LayoutProvider, "Layouts"),
					(CapabilityKinds.FolderViewProvider, "Folder Views"),
					(CapabilityKinds.WidgetTypeProvider, "Widgets"),
					(CapabilityKinds.Migration, "Migration")
				}));

			var kinds = dto.ProvidedCapabilities.Select(c => c.Kind).ToList();
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.Icons));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.ConfigFlow));
			Assert.That(kinds, Does.Not.Contain(CapabilityKinds.Issues));
		});
	}

	[Test]
	public async Task A_negotiated_kind_with_an_empty_describe_section_is_still_advertised()
	{
		var snapshot = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			AcceptedKinds = [CapabilityKinds.Actions, CapabilityKinds.MusicPlayer],
			Actions = [ActionDescriptor()],
			MusicPlayerInstances = []
		};
		var integration = CreateRemoteIntegration(snapshot);

		var dto = await HandleSingle(integration);

		Assert.That(dto.ProvidedCapabilities.Select(c => TestLocalization.Resolve(c.Name)),
			Is.EqualTo(_actionsMusicPlayerNames));
	}

	[Test]
	public async Task Capability_order_is_consistent_across_integrations()
	{
		var a = new FakeOrderIntegrationA();
		var b = new FakeOrderIntegrationB();
		var c = new FakeOrderIntegrationC();

		// Deliberately shuffled registration order.
		var registry = new ConfigurableIntegrationRegistry([c, a, b]);
		var handler = CreateHandler(registry);

		var response = await handler.Handle(new GetIntegrationsRequest(), CancellationToken.None);

		var kindsA = KindsFor(response, a.Id);
		var kindsB = KindsFor(response, b.Id);
		var kindsC = KindsFor(response, c.Id);

		Assert.Multiple(() =>
		{
			AssertSameRelativeOrder(kindsA, kindsB);
			AssertSameRelativeOrder(kindsA, kindsC);
			AssertSameRelativeOrder(kindsB, kindsC);
		});
	}

	private static List<string> KindsFor(GetIntegrationsResponse response, string integrationId)
		=> response.Integrations.Single(i => i.Id == integrationId).ProvidedCapabilities.Select(c => c.Kind).ToList();

	private static void AssertSameRelativeOrder(List<string> kindsX, List<string> kindsY)
	{
		var shared = kindsX.Where(kindsY.Contains).ToList();
		for (var i = 0; i < shared.Count; i++)
		{
			for (var j = i + 1; j < shared.Count; j++)
			{
				var xBefore = kindsX.IndexOf(shared[i]) < kindsX.IndexOf(shared[j]);
				var yBefore = kindsY.IndexOf(shared[i]) < kindsY.IndexOf(shared[j]);
				Assert.That(yBefore, Is.EqualTo(xBefore), $"{shared[i]} vs {shared[j]}");
			}
		}
	}

	private sealed class ThrowingInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("Not exercised by the overview capability tests.");

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => throw new NotSupportedException();

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class AlwaysDisconnected : IRemotePluginConnectionState
	{
		public bool IsConnected(string pluginId) => false;
	}
}
