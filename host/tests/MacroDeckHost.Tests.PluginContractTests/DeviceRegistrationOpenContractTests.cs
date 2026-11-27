using System.Collections.Concurrent;
using System.Text;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using DeviceSessionGuard = MacroDeckHost.Application.Auth.DeviceSessionGuard;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Devices.Surfaces.InProcess;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class DeviceRegistrationOpenContractTests : CapabilityContractFixture
{
	private const string ProviderDeviceId = "SERIAL-1";

	private const int Rows = 4;

	private const int Columns = 9;

	private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(10);

	private DeviceSurfaceWorld? _world;
	private RemoteDeviceProviderRegistry? _providers;
	private PluginCallbackRouter? _router;

	[TearDown]
	public void RegistrationTearDown()
	{
		_world?.Dispose();
		_world = null;
		_providers?.Dispose();
		_providers = null;
		_router = null;
	}

	[Test]
	public async Task A_plugin_that_registers_its_assigned_device_and_fetches_every_key_while_it_opens_gets_them_all()
	{
		var provider = new RegisteringProvider();
		var (world, deviceId) = await ConnectPluginAsync(provider);

		await provider.InitializeAsync(new RemoteDeviceProviderContext(CreatePluginHostInvoker())).WaitAsync(_deadline);
		await WaitForAsync(() => provider.Opened.Task.IsCompleted, "The session never opened.", _deadline);
		await WaitForAsync(() => provider.Surfaces.Count > 0, "The first surface never arrived.", _deadline);

		AssertEveryKeyArrived(world, provider, deviceId, provider.Registration!);
	}

	[Test]
	public async Task
		A_plugin_that_fetches_every_key_at_once_while_it_opens_gets_them_all_and_keeps_its_callback_budget()
	{
		var provider = new RegisteringProvider { Concurrently = true };
		var (world, deviceId) = await ConnectPluginAsync(provider);

		await provider.InitializeAsync(new RemoteDeviceProviderContext(CreatePluginHostInvoker())).WaitAsync(_deadline);
		await WaitForAsync(() => provider.Opened.Task.IsCompleted, "The session never opened.", _deadline);
		await WaitForAsync(() => provider.Surfaces.Count > 0, "The first surface never arrived.", _deadline);

		AssertEveryKeyArrived(world, provider, deviceId, provider.Registration!);
		Assert.That(_router!.Admit(PluginId,
				new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.List }),
			Is.Null,
			"the key fetches left the budget of the plugin's other callbacks alone");
	}

	[Test]
	public async Task A_plugin_that_fetched_every_key_while_offline_gets_every_key_again_newer_when_it_is_back()
	{
		var provider = new RegisteringProvider { Presence = DevicePresence.Offline };
		var (world, deviceId) = await ConnectPluginAsync(provider);
		var context = new RemoteDeviceProviderContext(CreatePluginHostInvoker());
		await provider.InitializeAsync(context).WaitAsync(_deadline);
		await WaitForAsync(() => provider.Opened.Task.IsCompleted, "The session never opened.", _deadline);
		var paintedWhileOffline = provider.Painted.Count;

		await context.SetDevicePresenceAsync(ProviderDeviceId, DevicePresence.Online).WaitAsync(_deadline);
		await WaitForAsync(() =>
			{
				world.Time.Advance(TimeSpan.FromMilliseconds(100));
				return provider.Painted.Count >= Rows * Columns;
			},
			"Not every key arrived when the device came back.",
			_deadline);

		var fetched = provider.Fetched.ToDictionary(image => (image!.Row, image.Column), image => image!.Revision);
		Assert.Multiple(() =>
		{
			Assert.That(world.Service.IsOpen(deviceId), Is.True);
			Assert.That(fetched, Has.Count.EqualTo(Rows * Columns));
			Assert.That(paintedWhileOffline, Is.Zero);
			Assert.That(provider.Painted.Select(image => (image.Row, image.Column)), Is.EquivalentTo(fetched.Keys));
			Assert.That(provider.Painted.Where(image => image.Revision <= fetched[(image.Row, image.Column)]),
				Is.Empty,
				"every key comes back newer than the picture fetched for it");
		});
	}

	private async Task<(DeviceSurfaceWorld World, Guid DeviceId)> ConnectPluginAsync(RegisteringProvider provider)
	{
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
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.DeviceProvider,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 3 }
				}
			],
			[CapabilityKinds.DeviceProvider],
			negotiatedCapabilityVersion: 3);

		_providers = new RemoteDeviceProviderRegistry(SessionRegistry, Invoker, sessionOwners, HostAssetSender);
		var (world, registry) = World(_providers.Resolve(PluginId)!, PluginId);
		var deviceId = await AssignedDeviceAsync(world, PluginId);
		router = CreateRouter(world, registry, sessionOwners);
		_router = router;
		return (world, deviceId);
	}

	[Test]
	public async Task An_in_process_provider_that_registers_while_it_initializes_and_fetches_every_key_gets_them_all()
	{
		var provider = new RegisteringProvider();
		IDeviceSurfaceService? service = null;
		var adapter = new InProcessDeviceSurfaceProvider("integration.keys",
			provider,
			() => service!,
			Serilog.Core.Logger.None);
		var (world, registry) = World(adapter, "integration.keys");
		service = world.Service;
		var deviceId = await AssignedDeviceAsync(world, "integration.keys");

		await provider.InitializeAsync(new RegistryContext(registry, "integration.keys")).WaitAsync(_deadline);
		await WaitForAsync(() => provider.Opened.Task.IsCompleted, "The session never opened.", _deadline);
		await WaitForAsync(() => provider.Surfaces.Count > 0, "The first surface never arrived.", _deadline);

		AssertEveryKeyArrived(world, provider, deviceId, provider.Registration!);
	}

	private static void AssertEveryKeyArrived(
		DeviceSurfaceWorld world,
		RegisteringProvider provider,
		Guid deviceId,
		DeviceRegistration registration)
	{
		var cells = provider.Fetched.Select(image => (image?.Row, image?.Column)).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(registration.DeviceId, Is.EqualTo(deviceId.ToString()));
			Assert.That(world.Service.IsOpen(deviceId), Is.True);
			Assert.That(provider.Opened.Task.Result, Is.Null, "the open handler finished without an error");
			Assert.That(provider.Fetched, Has.Count.EqualTo(Rows * Columns).And.All.Not.Null);
			Assert.That(cells, Is.EquivalentTo(Cells().Select(cell => ((int?)cell.Row, (int?)cell.Column))));
			Assert.That(provider.Fetched.Select(image => Encoding.UTF8.GetString(image!.Content.Span)),
				Has.Some.StartWith(nameof(DeviceKeyCellKind.Widget)));
		});
	}

	private static IEnumerable<(int Row, int Column)> Cells()
	{
		return Enumerable.Range(0, Rows)
			.SelectMany(row => Enumerable.Range(0, Columns).Select(column => (row, column)));
	}

	private static DeviceDescriptor Descriptor(DevicePresence presence)
	{
		return new DeviceDescriptor(ProviderDeviceId,
			"Key Deck",
			LayoutReference: ContractDeck.LayoutReference,
			Presence: presence);
	}

	private (DeviceSurfaceWorld World, PluginDeviceRegistry Registry) World(
		IDeviceSurfaceProvider provider,
		string providerId)
	{
		DeviceSurfaceWorld? world = null;
		var lane = new DeviceSessionLane(Serilog.Core.Logger.None);
		IServiceScopeFactory? scopes = null;
		var mediator = new RecordingMediator
		{
			OnPublish = (notification, cancellationToken) => notification switch
			{
				DeviceChangedNotification changed => new DeviceChangedDeviceSurfaceHandler(world!.Service,
						scopes!,
						lane)
					.Handle(changed, cancellationToken),
				DevicePresenceChangedNotification presence => new DevicePresenceChangedDeviceSurfaceHandler(
						world!.Service,
						lane)
					.Handle(presence, cancellationToken),
				_ => default
			}
		};

		world = new DeviceSurfaceWorld(provider,
			providerId,
			services => services.AddSingleton<IMediator>(mediator),
			new CellRenderer());
		_world = world;
		scopes = world.Scopes;
		var registry = new PluginDeviceRegistry(world.Scopes,
			world.Presence,
			new DeviceConnectionTracker(new DeviceSurfaceWorld.NoEventBus(),
				world.Time,
				new DeckClientTracker(Serilog.Core.Logger.None)),
			new DeviceSessionGuard(),
			new LayoutRegistry(new RecordingMediator()),
			world.Time);
		return (world, registry);
	}

	private static async Task<Guid> AssignedDeviceAsync(DeviceSurfaceWorld world, string providerId)
	{
		var deviceId = Guid.NewGuid();
		var device = ContractDeck.Device(deviceId, providerId, ProviderDeviceId);
		device.LayoutSnapshot = LayoutSnapshotSerializer.Serialize(new LayoutDescriptor(ContractDeck.LayoutReference,
			"Key Deck",
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = Rows,
						Columns = Columns,
						KeySize = new LayoutKeySize(72, 72),
						KeyImages = new LayoutKeyImageFormat { Encoding = KeyImageEncodings.Png }
					}
				}
			],
			new LayoutCapabilities { Visuals = LayoutVisualCapabilities.Full }));
		await world.Devices.Create(device);
		return deviceId;
	}

	private PluginCallbackRouter CreateRouter(
		DeviceSurfaceWorld world,
		IPluginDeviceRegistry registry,
		RemoteDeviceSessionRegistry sessionOwners)
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
			registry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			world.Service,
			sessionOwners,
			HostAssetSender);
	}

	private sealed class RegisteringProvider : IPluginIntegration, IDeviceProvider
	{
		private readonly ConcurrentQueue<DeviceSurface> _surfaces = new();
		private readonly ConcurrentQueue<DeviceKeyImage> _painted = new();

		public DevicePresence Presence { get; init; } = DevicePresence.Online;

		public bool Concurrently { get; init; }

		public IReadOnlyList<DeviceKeyImage> Painted => [.. _painted];

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public DeviceRegistration? Registration { get; private set; }

		public List<DeviceKeyImage?> Fetched { get; } = [];

		public IReadOnlyList<DeviceSurface> Surfaces => [.. _surfaces];

		public TaskCompletionSource<Exception?> Opened { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public async Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
		{
			Registration = await context.RegisterDeviceAsync(Descriptor(Presence), cancellationToken);
		}

		public Task ShutdownAsync(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public async Task OnSessionOpenedAsync(IDeviceSession session, CancellationToken cancellationToken = default)
		{
			session.SurfaceChanged += (_, args) => _surfaces.Enqueue(args.Surface);
			session.KeyImageChanged += (_, args) => _painted.Enqueue(args.Image);
			try
			{
				if (Concurrently)
				{
					Fetched.AddRange(await Task.WhenAll(Cells()
						.Select(cell => session.GetKeyImageAsync(cell.Row, cell.Column, cancellationToken))));
				}
				else
				{
					foreach (var (row, column) in Cells())
					{
						Fetched.Add(await session.GetKeyImageAsync(row, column, cancellationToken));
					}
				}

				Opened.TrySetResult(null);
			}
			catch (Exception exception)
			{
				Opened.TrySetResult(exception);
				throw;
			}
		}
	}

	private sealed class RegistryContext : IDeviceProviderContext
	{
		private readonly IPluginDeviceRegistry _registry;
		private readonly string _providerId;

		public RegistryContext(IPluginDeviceRegistry registry, string providerId)
		{
			_registry = registry;
			_providerId = providerId;
		}

		public Task<DeviceRegistration> RegisterDeviceAsync(
			DeviceDescriptor device,
			CancellationToken cancellationToken = default)
		{
			return _registry.RegisterAsync(_providerId, device, cancellationToken);
		}

		public Task UpdateDeviceAsync(DeviceDescriptor device, CancellationToken cancellationToken = default)
		{
			return _registry.UpdateAsync(_providerId, device, cancellationToken);
		}

		public Task SetDevicePresenceAsync(
			string deviceId,
			DevicePresence presence,
			CancellationToken cancellationToken = default)
		{
			return _registry.SetPresenceAsync(_providerId, deviceId, presence, cancellationToken);
		}

		public Task UnregisterDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
		{
			return _registry.UnregisterAsync(_providerId, deviceId, cancellationToken);
		}
	}

	private sealed class CellRenderer : IDeviceKeyImageRenderer
	{
		public event EventHandler? FontsChanged
		{
			add { }
			remove { }
		}

		public DeviceKeyImageRenderResult Render(DeviceKeyImageRenderRequest request)
		{
			var keys = Enumerable.Range(0, request.Rows)
				.SelectMany(row => Enumerable.Range(0, request.Columns)
					.Select(column => new DeviceKeyImagePicture(row,
						column,
						Encoding.UTF8.GetBytes($"{request.Kind}|{request.WidgetType}|{row},{column}"),
						KeyImageContentTypes.Png)))
				.ToList();
			return new DeviceKeyImageRenderResult(keys, null, []);
		}
	}
}
