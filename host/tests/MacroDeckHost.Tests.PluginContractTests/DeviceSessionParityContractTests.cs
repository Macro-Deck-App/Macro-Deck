using System.Text;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Devices.Surfaces.InProcess;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// The issue's walkthrough - register a fixed-grid device, render its assigned profile, navigate, watch
/// a state change arrive, press and release - run once in process and once across the plugin wire, and
/// asserted against one expected log rather than two independently written assertion sets. Two sets
/// would drift and both keep passing while the paths diverged, which is the exact failure parity is
/// supposed to catch.
/// </summary>
[TestFixture]
internal sealed class DeviceSessionParityContractTests : CapabilityContractFixture
{
	private const string ProviderDeviceId = "SERIAL-1";

	/// <summary>
	/// What the walkthrough must look like from the provider's seat, whichever side of the transport it
	/// sits on. The grid is the profile's 3x5 - never the device's own 3x2, which is echoed back
	/// untouched as the layout reference because no reflow is in scope.
	/// </summary>
	private static readonly string[] _expectedObservations =
	[
		"surface rev=1 profile=Studio folder=Home grid=3x5 ref=com.example.contract::3x2 widgets=11111111-2222-3333-4444-555555555555@1,0",
		"surface rev=2 profile=Studio folder=Lights grid=3x5 ref=com.example.contract::3x2 widgets=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@0,1",
		"surface rev=3 profile=Studio folder=Lights grid=3x5 ref=com.example.contract::3x2 widgets=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@0,1 state=on/Recording",
		"trigger aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee onTouchStart",
		"trigger aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee onTouchEnd",
		"trigger aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee onShortPress"
	];

	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.DeviceProvider,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 2 }
		};

	private static string Describe(DeviceSurface surface)
	{
		var widgets = string.Join(",",
			surface.Widgets.Select(widget => $"{widget.Id}@{widget.PositionX},{widget.PositionY}"));
		var state = surface.Widgets.Select(widget => widget.StateId is null
					? string.Empty
					: $" state={widget.StateId}/{widget.StateLabel}")
				.FirstOrDefault(value => value.Length > 0) ??
			string.Empty;

		return $"surface rev={surface.Revision} profile={surface.Profile?.Name} folder={surface.Folder?.Name} " +
			$"grid={surface.Layout.Rows}x{surface.Layout.Columns} ref={surface.Layout.LayoutReference} " +
			$"widgets={widgets}{state}";
	}

	/// <summary>Steps 3 to 7 of the walkthrough, written once. Steps 1 and 2 - registering the device and
	/// its assigned profile - are what <see cref="DeviceSurfaceWorld.RegisterAndOpenAsync" /> does.</summary>
	private static async Task<IReadOnlyList<string>> RunAsync(
		DeviceSurfaceWorld world,
		WalkthroughProvider provider,
		Guid deviceId)
	{
		await WaitForAsync(() => provider.Surfaces.Count == 1,
			"the provider never received its first surface");

		await world.Service.NavigateAsync(deviceId,
			DeckNavigationCommand.ChangeTo(ContractDeck.LightsFolderId, ContractDeck.ProfileId));
		await WaitForAsync(() => provider.Surfaces.Count == 2,
			"the navigation never reached the provider");

		world.WidgetStates.Set(ContractDeck.LightsWidgetId, "on", "Recording");
		await world.Service.InvalidateAsync(deviceId);
		world.Time.Advance(TimeSpan.FromSeconds(5));
		await WaitForAsync(() => provider.Surfaces.Count == 3,
			"the state change never reached the provider");

		var widgetId = ContractDeck.LightsWidgetId.ToString();
		await provider.SendAsync(DeviceInteractionKind.Press, widgetId);
		await provider.SendAsync(DeviceInteractionKind.Release, widgetId);
		await WaitForAsync(() => world.Triggers.Requests.Count == 3,
			"the host pipeline never ran the full press");

		return
		[
			.. provider.Surfaces.Select(Describe),
			.. world.Triggers.Requests.Select(request => $"trigger {request.WidgetId} {request.TriggerType}")
		];
	}

	[Test]
	public async Task An_in_process_provider_walks_the_deck_the_way_the_issue_describes()
	{
		var provider = new WalkthroughProvider();
		var adapter = new InProcessDeviceSurfaceProvider("integration.walkthrough",
			provider,
			() => _world!.Service,
			Serilog.Core.Logger.None);

		using var world = new DeviceSurfaceWorld(adapter, "integration.walkthrough");
		_world = world;

		var deviceId = await world.RegisterAndOpenAsync(ProviderDeviceId);

		Assert.That(await RunAsync(world, provider, deviceId), Is.EqualTo(_expectedObservations));
	}

	[Test]
	public async Task A_plugin_across_the_wire_walks_the_same_deck_to_the_same_log()
	{
		var provider = new WalkthroughProvider();
		var sessionOwners = new RemoteDeviceSessionRegistry();

		// Installed before the connection opens: the link captures the handler as it is built, and the
		// router it routes into needs the surface service that only exists once the plugin is connected.
		PluginCallbackRouter? router = null;
		HostInvokeHandler = (_, payload, cancellationToken)
			=> router!.RouteAsync(PluginId, Guid.NewGuid().ToString(), payload, cancellationToken);

		await ConnectAsync([
				new DeviceProviderCapabilityHandler([provider],
					TestMetadata.Default,
					CreatePluginHostInvoker(),
					PluginHostAssets)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: 2);

		using var providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, sessionOwners);
		using var world = new DeviceSurfaceWorld(providers.Resolve(PluginId)!, PluginId);
		_world = world;
		router = CreateRouter(world, sessionOwners);

		var deviceId = await world.RegisterAndOpenAsync(ProviderDeviceId);

		Assert.That(await RunAsync(world, provider, deviceId), Is.EqualTo(_expectedObservations));
	}

	[Test]
	public async Task A_whole_press_on_a_plugin_served_tile_is_accepted_across_the_wire_while_its_tree_is_pending()
	{
		var provider = new WalkthroughProvider();
		var sessionOwners = new RemoteDeviceSessionRegistry();

		PluginCallbackRouter? router = null;
		HostInvokeHandler = (_, payload, cancellationToken)
			=> router!.RouteAsync(PluginId, Guid.NewGuid().ToString(), payload, cancellationToken);

		await ConnectAsync([
				new DeviceProviderCapabilityHandler([provider],
					TestMetadata.Default,
					CreatePluginHostInvoker(),
					PluginHostAssets)
			],
			[Provider()],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: 2);

		var tree = new PendingTreeUiSessionBroker();
		using var providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, sessionOwners);
		using var world = new DeviceSurfaceWorld(providers.Resolve(PluginId)!,
			PluginId,
			services =>
			{
				services.AddSingleton<IUiSessionBroker>(tree);
				services.AddSingleton<IWidgetUiSessionOpener>(new AcceptingWidgetUiSessionOpener());
			});
		_world = world;
		world.Profiles.GetFoldersForProfile(ContractDeck.ProfileId)
			.Single(folder => folder.Id == ContractDeck.FolderId)
			.Widgets.Single().Type = "com.example.contract.meter";
		router = CreateRouter(world, sessionOwners);

		await world.RegisterAndOpenAsync(ProviderDeviceId);
		await WaitForAsync(() => provider.Surfaces.Count == 1, "the provider never received its first surface");

		var result = await provider.SendAsync(DeviceInteractionKind.ShortPress, ContractDeck.WidgetId.ToString());
		var ranBeforeTheTree = world.Triggers.Requests.Count;
		tree.Tree.SetResult(new UiRawJson(Encoding.UTF8.GetBytes(
			"""{"revision":1,"surface":{"kind":"widget"},"root":{"id":"label","type":"ui.text"}}""")));
		await WaitForAsync(() => world.Triggers.Requests.Count == 1, "the queued press never ran the tile's flow");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(DeviceInteractionStatus.Accepted));
			Assert.That(ranBeforeTheTree, Is.Zero);
			Assert.That(world.Triggers.Requests.Single().TriggerType, Is.EqualTo("onShortPress"));
		});
	}

	private PluginCallbackRouter CreateRouter(DeviceSurfaceWorld world, RemoteDeviceSessionRegistry sessionOwners)
	{
		var services = new ServiceCollection().BuildServiceProvider();
		return new PluginCallbackRouter(SessionRegistry,
			Invoker,
			services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			world.Service,
			sessionOwners,
			HostAssetSender);
	}

	private DeviceSurfaceWorld? _world;

	/// <summary>A hardware plugin's whole job: keep the session, render every surface, report presses.</summary>
	private sealed class WalkthroughProvider : IPluginIntegration, IDeviceProvider
	{
		private IDeviceSession? _session;

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public List<DeviceSurface> Surfaces { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public string ProviderName => "Walkthrough Deck";

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public IReadOnlyList<DeviceDescriptor> GetDevices()
			=>
			[
				new(ProviderDeviceId,
					"Walkthrough Deck",
					null,
					null,
					ContractDeck.LayoutReference,
					new DeviceCapabilities { KeyCount = 6 })
			];

		public Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			_session = session;
			session.SurfaceChanged += (_, args) => Surfaces.Add(args.Surface);
			return Task.CompletedTask;
		}

		public Task<DeviceInteractionResult> SendAsync(DeviceInteractionKind kind, string widgetId)
			=> _session!.SendInteractionAsync(new DeviceInteraction
			{
				Kind = kind,
				Target = new DeviceInteractionTarget { WidgetId = widgetId },
				SurfaceRevision = _session.CurrentSurface.Revision
			});
	}
}
