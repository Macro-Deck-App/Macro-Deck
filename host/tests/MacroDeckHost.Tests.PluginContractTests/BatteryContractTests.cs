using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class BatteryContractTests
{
	private const string PluginId = "com.example.batteries";

	private BatteryRegistry _registry = null!;
	private PluginConnectionState _state = null!;
	private HostStateCache _stateCache = null!;
	private CallbackHostLink _link = null!;
	private Task<ConnectionOutcome>? _run;
	private RemoteBatteryProviderContext _provider = null!;
	private RemoteBatteryApi _reader = null!;
	private long _revision;

	[SetUp]
	public async Task SetUp()
	{
		var sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		await sessions.Create(new PluginSessionRecord
		{
			SessionId = "session-batteries",
			PluginId = PluginId,
			DisplayName = "Batteries",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal)
			{
				[CapabilityKinds.BatteryProvider]
					= CapabilityNegotiationResult.Accept(CapabilityKinds.BatteryProvider, 1)
			},
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});

		_registry = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		var router = new PluginCallbackRouter(sessions,
			new CallbackFakeCapabilityInvoker(),
			scopeFactory,
			new CallbackFakeNotificationStore(),
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			new RecordingDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			batteries: _registry);

		_state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(_state, TimeProvider.System, Serilog.Core.Logger.None);
		_stateCache = new HostStateCache(_state);
		_link = new CallbackHostLink(PluginId, "session-batteries", router, new CallbackFakeEventBus());

		var connection = new PluginSessionConnection(_link,
			PluginTestSession.Create("session-batteries", 1, DateTimeOffset.UnixEpoch),
			PluginTestSession.Dispatcher([], _state, TimeProvider.System),
			_state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			_stateCache);

		_state.ActiveConnection = connection;
		_run = connection.RunAsync(null, "battery-fixture", CancellationToken.None);
		await WaitForAsync(() => _state.IsReady);

		_provider = new RemoteBatteryProviderContext(hostInvoker, _state, Serilog.Core.Logger.None);
		_reader = new RemoteBatteryApi(_stateCache, Serilog.Core.Logger.None);
		_registry.Changed += OnRegistryChanged;
	}

	[TearDown]
	public async Task TearDown()
	{
		_registry.Changed -= OnRegistryChanged;
		_reader.Dispose();
		_provider.Dispose();
		_registry.Dispose();
		if (_run is not null)
		{
			await _link.DisposeAsync();
			await _run;
			_run.Dispose();
		}
	}

	[Test]
	public async Task A_published_batch_reaches_the_host_under_the_plugins_id_with_every_field()
	{
		await _provider.PublishAsync([
			new BatteryDevice
			{
				Id = "pixel",
				Name = "Pixel 8",
				Kind = BatteryDeviceKinds.Phone,
				LevelPercent = 64,
				ChargeState = BatteryChargeState.Charging,
				TimeToFull = TimeSpan.FromMinutes(40),
				Health = BatteryHealth.Good
			},
			new BatteryDevice { Id = "buds", Name = "Buds", Kind = BatteryDeviceKinds.Headphones, IsConnected = false }
		]);

		var pixel = _registry.Find($"{PluginId}:pixel");
		Assert.Multiple(() =>
		{
			Assert.That(_registry.Devices, Has.Count.EqualTo(2));
			Assert.That(pixel?.ProviderId, Is.EqualTo(PluginId));
			Assert.That(pixel?.LevelPercent, Is.EqualTo(64));
			Assert.That(pixel?.ChargeState, Is.EqualTo(BatteryChargeState.Charging));
			Assert.That(pixel?.TimeToFull, Is.EqualTo(TimeSpan.FromMinutes(40)));
			Assert.That(pixel?.Health, Is.EqualTo(BatteryHealth.Good));
			Assert.That(_registry.Find($"{PluginId}:buds")?.IsConnected, Is.False);
		});
	}

	[Test]
	public async Task A_reading_plugin_sees_published_devices_and_is_told_which_were_removed_for_good()
	{
		var removals = new List<IReadOnlyList<string>>();
		_reader.Changed += (_, args) =>
		{
			lock (removals)
			{
				removals.Add(args.RemovedIds);
			}
		};

		await _provider.PublishAsync(new BatteryDevice { Id = "pixel", Name = "Pixel 8", LevelPercent = 64 });
		await WaitForAsync(() => _reader.Find($"{PluginId}:pixel") is not null);
		var seen = _reader.Find($"{PluginId}:pixel");

		await _provider.RemoveAsync("pixel");
		await WaitForAsync(() => _reader.Devices.Count == 0 && Removals().Any(ids => ids.Count > 0));

		Assert.Multiple(() =>
		{
			Assert.That(seen?.LevelPercent, Is.EqualTo(64));
			Assert.That(seen?.ProviderId, Is.EqualTo(PluginId));
			Assert.That(Removals().Single(ids => ids.Count > 0), Is.EqualTo(new[] { $"{PluginId}:pixel" }));
		});

		List<IReadOnlyList<string>> Removals()
		{
			lock (removals)
			{
				return [.. removals];
			}
		}
	}

	[Test]
	public async Task A_provider_initialized_again_with_fewer_devices_leaves_no_stale_device_and_reports_none_removed()
	{
		var removals = new List<IReadOnlyList<string>>();
		_reader.Changed += (_, args) =>
		{
			lock (removals)
			{
				removals.Add(args.RemovedIds);
			}
		};
		await _provider.PublishAsync([
			new BatteryDevice { Id = "pixel", Name = "Pixel 8" },
			new BatteryDevice { Id = "buds", Name = "Buds" },
			new BatteryDevice { Id = "watch", Name = "Watch" }
		]);
		await WaitForAsync(() => _reader.Devices.Count == 3);

		await _provider.ResetAsync();
		await _provider.PublishAsync(new BatteryDevice { Id = "pixel", Name = "Pixel 8", LevelPercent = 50 });
		await WaitForAsync(() => _reader.Devices.Count == 1 && _reader.Devices[0].LevelPercent == 50);

		Assert.Multiple(() =>
		{
			Assert.That(_registry.Devices.Select(device => device.Id), Is.EqualTo(new[] { $"{PluginId}:pixel" }));
			lock (removals)
			{
				Assert.That(removals.SelectMany(ids => ids), Is.Empty);
			}
		});
	}

	private void OnRegistryChanged(object? sender, BatteryDevicesChangedEventArgs e)
	{
		_link.PushHostState(HostApis.Batteries,
			new BatteryStateDto
			{
				Devices = [.. _registry.Devices.Select(device => BatteryWire.ToDto(device))],
				RemovedIds = e.RemovedIds,
				Revision = Interlocked.Increment(ref _revision)
			});
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail("The condition was never met.");
	}
}
