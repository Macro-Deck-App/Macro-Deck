using System.Reflection;
using System.Text.Json;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.MusicPlayer;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class RemotePluginIntegrationFactoryTests
{
	private static readonly IPluginCapabilityInvoker _invoker = new ThrowingInvoker();
	private static readonly IRemotePluginConnectionState _connectionState = new AlwaysDisconnected();
	private static readonly IPluginAssetCache _assetCache = new InMemoryPluginAssetCache();

	private static RemotePluginIntegration CreateIntegration(bool icon, bool configFlow, bool dynamicEventOptions)
		=> RemotePluginIntegrationFactory.Create("com.example.plugin",
			"Example",
			"1.0.0",
			RemotePluginCapabilitySnapshot.Empty("com.example.plugin"),
			_invoker,
			_connectionState,
			_assetCache,
			icon,
			configFlow,
			dynamicEventOptions);

	[TestCase(false, false, false)]
	[TestCase(true, false, false)]
	[TestCase(false, true, false)]
	[TestCase(false, false, true)]
	[TestCase(true, true, false)]
	[TestCase(true, false, true)]
	[TestCase(false, true, true)]
	[TestCase(true, true, true)]
	public void Integration_leaves_implement_exactly_the_declared_interfaces(
		bool icon,
		bool configFlow,
		bool dynamicEventOptions)
	{
		var integration = CreateIntegration(icon, configFlow, dynamicEventOptions);

		Assert.Multiple(() =>
		{
			Assert.That(integration is IIntegrationIconProvider, Is.EqualTo(icon), nameof(IIntegrationIconProvider));
			Assert.That(integration is IConfigFlowProvider, Is.EqualTo(configFlow), nameof(IConfigFlowProvider));
			Assert.That(integration is IDynamicEventOptionsProvider,
				Is.EqualTo(dynamicEventOptions),
				nameof(IDynamicEventOptionsProvider));

			Assert.That(integration, Is.InstanceOf<MacroDeck.Sdk.Variables.IVariableProvider>());
			Assert.That(integration, Is.InstanceOf<IEventProvider>());
			Assert.That(integration, Is.InstanceOf<IMusicPlayerProvider>());
			Assert.That(integration, Is.InstanceOf<MacroDeck.Sdk.Weather.IWeatherProvider>());
			Assert.That(integration, Is.InstanceOf<MacroDeck.Sdk.Profiles.IProfileProvider>());
			Assert.That(integration, Is.InstanceOf<MacroDeck.Sdk.Issues.IIntegrationIssueProvider>());
		});
	}

	[Test]
	public void A_plugin_reporting_dynamic_event_options_gets_the_leaf_that_implements_the_interface()
	{
		var integration = CreateIntegration(icon: false, configFlow: false, dynamicEventOptions: true);

		Assert.That(integration, Is.InstanceOf<IDynamicEventOptionsProvider>());
	}

	[Test]
	public void A_plugin_not_reporting_dynamic_event_options_does_not_get_that_leaf()
	{
		var integration = CreateIntegration(icon: false, configFlow: false, dynamicEventOptions: false);

		Assert.That(integration, Is.Not.InstanceOf<IDynamicEventOptionsProvider>());
	}

	private static RemoteActionDescriptor Descriptor(bool supportsDynamicOptions, bool providesState)
		=> new("play", "Play", string.Empty, [], null, supportsDynamicOptions, providesState);

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(true, true)]
	public void Action_leaves_implement_exactly_the_declared_interfaces(bool dynamicOptions, bool providesState)
	{
		var descriptor = Descriptor(dynamicOptions, providesState);
		var action = RemoteActionDefinitionFactory.Create(_invoker, "com.example.plugin", descriptor);

		Assert.Multiple(() =>
		{
			Assert.That(action is IDynamicOptionsActionDefinition,
				Is.EqualTo(dynamicOptions),
				nameof(IDynamicOptionsActionDefinition));
			Assert.That(action is IStateProviderActionDefinition,
				Is.EqualTo(providesState),
				nameof(IStateProviderActionDefinition));

			Assert.That(action, Is.InstanceOf<IConfigurableActionDefinition>());
		});
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(false, true)]
	[TestCase(true, true)]
	public void MusicPlayer_leaves_implement_exactly_the_declared_interfaces(bool catalog, bool devices)
	{
		var player = RemoteMusicPlayerFactory.Create("com.example.plugin",
			"instance-1",
			_invoker,
			_assetCache,
			catalog,
			devices);

		Assert.Multiple(() =>
		{
			Assert.That(player is ICatalogMusicPlayer, Is.EqualTo(catalog), nameof(ICatalogMusicPlayer));
			Assert.That(player is IMusicPlayerCatalogProvider,
				Is.EqualTo(catalog),
				nameof(IMusicPlayerCatalogProvider));
			Assert.That(player is IMusicPlayerDeviceProvider, Is.EqualTo(devices), nameof(IMusicPlayerDeviceProvider));
			Assert.That(player, Is.InstanceOf<IMusicPlayer>());

			if (!catalog)
			{
				Assert.That(player.GetType().GetMethod("PlayItemAsync", BindingFlags.Public | BindingFlags.Instance),
					Is.Null);
			}
		});
	}

	[Test]
	public void GetPlayer_picks_the_leaf_per_instance_from_the_snapshot_not_one_flag_pair_for_the_whole_plugin()
	{
		var snapshot = RemotePluginCapabilitySnapshot.Empty("com.example.plugin") with
		{
			MusicPlayerInstances =
			[
				new MusicPlayerInstance("catalog-only", "Catalog Only"),
				new MusicPlayerInstance("devices-only", "Devices Only"),
				new MusicPlayerInstance("plain", "Plain")
			],
			MusicPlayerCatalogInstanceIds = ["catalog-only"],
			MusicPlayerDeviceInstanceIds = ["devices-only"]
		};

		var integration = RemotePluginIntegrationFactory.Create("com.example.plugin",
			"Example",
			"1.0.0",
			snapshot,
			_invoker,
			_connectionState,
			_assetCache,
			hasIcon: false,
			hasConfigFlow: false,
			hasDynamicEventOptions: false);

		Assert.Multiple(() =>
		{
			Assert.That(integration.GetPlayer("catalog-only"), Is.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(integration.GetPlayer("catalog-only"), Is.Not.InstanceOf<IMusicPlayerDeviceProvider>());
			Assert.That(integration.GetPlayer("devices-only"), Is.InstanceOf<IMusicPlayerDeviceProvider>());
			Assert.That(integration.GetPlayer("devices-only"), Is.Not.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(integration.GetPlayer("plain"), Is.Not.InstanceOf<IMusicPlayerCatalogProvider>());
			Assert.That(integration.GetPlayer("plain"), Is.Not.InstanceOf<IMusicPlayerDeviceProvider>());

			Assert.That(integration.GetPlayer("gone"), Is.Null);
		});
	}

	private sealed class ThrowingInvoker : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException("Not exercised by the presence-matrix tests.");

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope result)
			=> throw new NotSupportedException();

		public void AbortAll(string pluginId, MacroDeck.Plugin.Protocol.Errors.ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}

	private sealed class AlwaysDisconnected : IRemotePluginConnectionState
	{
		public bool IsConnected(string pluginId) => false;
	}
}
