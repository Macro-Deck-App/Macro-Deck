using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
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
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Batteries;

[TestFixture]
internal sealed class BatteryCallbackRoutingTests
{
	private const string PluginId = "com.example.batteries";
	private const string SessionId = "session-1";

	private PluginSessionRegistry _sessions = null!;
	private BatteryRegistry _batteries = null!;
	private PluginCallbackRouter _router = null!;

	[SetUp]
	public void SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_router = new PluginCallbackRouter(_sessions,
			null!,
			scopeFactory,
			null!,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			null!,
			new NoOpUiSessionSink(),
			new FakePluginDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			null!,
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new FakeHostLockState(),
			Serilog.Core.Logger.None,
			batteries: _batteries);
	}

	[TearDown]
	public void TearDown()
	{
		_batteries.Dispose();
	}

	[Test]
	public async Task A_declared_provider_publishes_under_its_own_plugin_id()
	{
		await OpenSessionAsync(true);

		var result = await PublishAsync(Device("phone", 55));

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(_batteries.Find($"{PluginId}:phone")?.LevelPercent, Is.EqualTo(55));
		});
	}

	[Test]
	public async Task A_plugin_that_did_not_declare_the_capability_is_refused_and_nothing_is_stored()
	{
		await OpenSessionAsync(false);

		var result = await PublishAsync(Device("phone"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(_batteries.Devices, Is.Empty);
		});
	}

	[Test]
	public async Task One_invalid_device_refuses_the_whole_publish()
	{
		await OpenSessionAsync(true);

		var result = await PublishAsync(Device("phone", 40), Device("watch", 150));

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(_batteries.Devices, Is.Empty);
		});
	}

	[Test]
	public async Task A_publish_of_more_than_64_devices_is_refused_and_64_fit_in_one_call()
	{
		await OpenSessionAsync(true);

		var tooMany = await PublishAsync([.. Enumerable.Range(0, 65).Select(index => Device($"d{index}"))]);
		var allowed = await PublishAsync([.. Enumerable.Range(0, 64).Select(index => Device($"d{index}"))]);

		Assert.Multiple(() =>
		{
			Assert.That(tooMany.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(allowed.Error, Is.Null);
			Assert.That(_batteries.Devices, Has.Count.EqualTo(64));
		});
	}

	[Test]
	public async Task An_invalid_sync_keeps_the_previous_set()
	{
		await OpenSessionAsync(true);
		await PublishAsync(Device("phone"));

		var result = await InvokeAsync(HostOperations.Batteries.Sync,
			new BatteriesSyncArguments { Devices = [Device("watch"), Device("watch")] });

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(_batteries.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "phone" }));
		});
	}

	[Test]
	public async Task Remove_and_sync_change_only_the_calling_plugins_devices()
	{
		await OpenSessionAsync(true);
		_batteries.Publish("com.example.other", null, [new BatteryDevice { Id = "phone", Name = "Other" }]);
		await PublishAsync(Device("phone"), Device("watch"));

		await InvokeAsync(HostOperations.Batteries.Remove, new BatteriesRemoveArguments { DeviceIds = ["phone"] });
		await InvokeAsync(HostOperations.Batteries.Sync, new BatteriesSyncArguments { Devices = [Device("buds")] });

		Assert.That(_batteries.Devices.Select(device => device.Id),
			Is.EquivalentTo(new[] { "com.example.other:phone", $"{PluginId}:buds" }));
	}

	[Test]
	public async Task A_publish_is_tagged_with_its_session_so_the_session_end_withdraws_it()
	{
		await OpenSessionAsync(true);
		await PublishAsync(Device("phone"));

		_batteries.RemoveSession(PluginId, SessionId);

		Assert.That(_batteries.Devices, Is.Empty);
	}

	[Test]
	public async Task Withdraw_hides_the_calling_sessions_devices_without_reporting_them_removed()
	{
		await OpenSessionAsync(true);
		_batteries.Publish("com.example.other", null, [new BatteryDevice { Id = "phone", Name = "Other" }]);
		await PublishAsync(Device("phone"), Device("watch"));
		var changes = new List<BatteryDevicesChangedEventArgs>();
		_batteries.Changed += (_, args) =>
		{
			lock (changes)
			{
				changes.Add(args);
			}
		};

		var result = await InvokeAsync(HostOperations.Batteries.Withdraw, null);
		await WaitForAsync(() => Changes().Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(_batteries.Devices.Select(device => device.Id),
				Is.EqualTo(new[] { "com.example.other:phone" }));
			Assert.That(Changes().SelectMany(change => change.RemovedIds), Is.Empty);
		});

		List<BatteryDevicesChangedEventArgs> Changes()
		{
			lock (changes)
			{
				return [.. changes];
			}
		}
	}

	[Test]
	public async Task Withdraw_from_an_ended_session_keeps_what_the_next_session_published()
	{
		await OpenSessionAsync(true);
		_batteries.Publish(PluginId, "session-2", [new BatteryDevice { Id = "phone", Name = "Phone" }]);

		await InvokeAsync(HostOperations.Batteries.Withdraw, null);

		Assert.That(_batteries.Devices.Select(device => device.LocalId), Is.EqualTo(new[] { "phone" }));
	}

	[Test]
	public async Task Withdraw_requires_the_battery_provider_capability()
	{
		await OpenSessionAsync(false);
		_batteries.Publish(PluginId, SessionId, [new BatteryDevice { Id = "phone", Name = "Phone" }]);

		var result = await InvokeAsync(HostOperations.Batteries.Withdraw, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(_batteries.Devices, Has.Count.EqualTo(1));
		});
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(5);
		}

		Assert.That(condition(), Is.True, "The condition was never met.");
	}

	private async Task OpenSessionAsync(bool declared)
	{
		var capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal);
		if (declared)
		{
			capabilities[CapabilityKinds.BatteryProvider]
				= CapabilityNegotiationResult.Accept(CapabilityKinds.BatteryProvider, 1);
		}

		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = SessionId,
			PluginId = PluginId,
			DisplayName = "Batteries",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 1,
			Capabilities = capabilities,
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
	}

	private Task<HostCallbackResult> PublishAsync(params BatteryDeviceDto[] devices)
	{
		return InvokeAsync(HostOperations.Batteries.Publish, new BatteriesPublishArguments { Devices = devices });
	}

	private Task<HostCallbackResult> InvokeAsync(string operation, object? arguments)
	{
		return _router.RouteAsync(PluginId,
			SessionId,
			Guid.CreateVersion7().ToString(),
			new HostInvokePayload
			{
				Api = HostApis.Batteries,
				Operation = operation,
				Arguments = arguments is null
					? null
					: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
			},
			CancellationToken.None);
	}

	private static BatteryDeviceDto Device(string id, int? level = null)
	{
		return new BatteryDeviceDto { Id = id, Name = id, Kind = BatteryDeviceKinds.Phone, LevelPercent = level };
	}
}
