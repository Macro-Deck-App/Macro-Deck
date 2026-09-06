using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Integrations.Adb;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbGatewayTests
{
	private static FakeAdbManager EnabledManager() => new()
	{
		Status = AdbStatus.Disabled with { Enabled = true, DefaultDeviceSerial = "device-1" }
	};

	private static AdbGateway CreateGateway(FakeAdbManager manager)
		=> new(manager, new LoggerConfiguration().CreateLogger());

	private static AdbDevice PlainDevice(string serial, AdbDeviceState state = AdbDeviceState.Device)
		=> new(serial, state, null, null, null, null, null, DateTimeOffset.UtcNow);

	private static T SingleCommand<T>(FakeAdbManager manager)
		where T : AdbCommand
	{
		Assert.That(manager.ExecutedCommands, Has.Count.EqualTo(1));
		Assert.That(manager.ExecutedCommands[0], Is.InstanceOf<T>());
		return (T)manager.ExecutedCommands[0];
	}

	[Test]
	public async Task SendKeyAsync_builds_a_key_event_command_with_the_serial_and_mapped_key()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.SendKeyAsync("device-1", AdbGatewayKey.Home, CancellationToken.None);

		var command = SingleCommand<AdbKeyEventCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Key, Is.EqualTo(AdbKey.Home));
		});
	}

	[TestCase(AdbGatewayKey.Home, AdbKey.Home)]
	[TestCase(AdbGatewayKey.Back, AdbKey.Back)]
	[TestCase(AdbGatewayKey.Enter, AdbKey.Enter)]
	[TestCase(AdbGatewayKey.Menu, AdbKey.Menu)]
	[TestCase(AdbGatewayKey.Search, AdbKey.Search)]
	[TestCase(AdbGatewayKey.VolumeUp, AdbKey.VolumeUp)]
	[TestCase(AdbGatewayKey.VolumeDown, AdbKey.VolumeDown)]
	[TestCase(AdbGatewayKey.VolumeMute, AdbKey.VolumeMute)]
	[TestCase(AdbGatewayKey.MediaPlayPause, AdbKey.MediaPlayPause)]
	[TestCase(AdbGatewayKey.MediaNext, AdbKey.MediaNext)]
	[TestCase(AdbGatewayKey.MediaPrevious, AdbKey.MediaPrevious)]
	[TestCase(AdbGatewayKey.Power, AdbKey.Power)]
	[TestCase(AdbGatewayKey.Sleep, AdbKey.Sleep)]
	[TestCase(AdbGatewayKey.Wakeup, AdbKey.Wakeup)]
	[TestCase(AdbGatewayKey.AppSwitch, AdbKey.AppSwitch)]
	[TestCase(AdbGatewayKey.DpadUp, AdbKey.DpadUp)]
	[TestCase(AdbGatewayKey.DpadDown, AdbKey.DpadDown)]
	[TestCase(AdbGatewayKey.DpadLeft, AdbKey.DpadLeft)]
	[TestCase(AdbGatewayKey.DpadRight, AdbKey.DpadRight)]
	[TestCase(AdbGatewayKey.DpadCenter, AdbKey.DpadCenter)]
	public async Task SendKeyAsync_maps_every_gateway_key_to_the_matching_adb_key(AdbGatewayKey gatewayKey,
		AdbKey adbKey)
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.SendKeyAsync("device-1", gatewayKey, CancellationToken.None);

		Assert.That(SingleCommand<AdbKeyEventCommand>(manager).Key, Is.EqualTo(adbKey));
	}

	[Test]
	public async Task StartAppAsync_builds_a_start_app_command_with_the_package()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.StartAppAsync("device-1", "com.example.app", CancellationToken.None);

		var command = SingleCommand<AdbStartAppCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Package, Is.EqualTo("com.example.app"));
		});
	}

	[Test]
	public async Task ForceStopAppAsync_builds_a_force_stop_command_with_the_package()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.ForceStopAppAsync("device-1", "com.example.app", CancellationToken.None);

		var command = SingleCommand<AdbForceStopAppCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Package, Is.EqualTo("com.example.app"));
		});
	}

	[Test]
	public async Task OpenUriAsync_builds_an_open_uri_command_with_the_uri()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.OpenUriAsync("device-1", "https://example.com", CancellationToken.None);

		var command = SingleCommand<AdbOpenUriCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Uri, Is.EqualTo("https://example.com"));
		});
	}

	[Test]
	public async Task InputTextAsync_builds_an_input_text_command_with_the_text()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.InputTextAsync("device-1", "hello world", CancellationToken.None);

		var command = SingleCommand<AdbInputTextCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Text, Is.EqualTo("hello world"));
		});
	}

	[Test]
	public async Task TapAsync_builds_a_tap_command_with_the_coordinates()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.TapAsync("device-1", 100, 200, CancellationToken.None);

		var command = SingleCommand<AdbTapCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.X, Is.EqualTo(100));
			Assert.That(command.Y, Is.EqualTo(200));
		});
	}

	[Test]
	public async Task SwipeAsync_builds_a_swipe_command_with_the_coordinates_and_duration()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.SwipeAsync("device-1", 10, 20, 30, 40, 250, CancellationToken.None);

		var command = SingleCommand<AdbSwipeCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.X1, Is.EqualTo(10));
			Assert.That(command.Y1, Is.EqualTo(20));
			Assert.That(command.X2, Is.EqualTo(30));
			Assert.That(command.Y2, Is.EqualTo(40));
			Assert.That(command.DurationMs, Is.EqualTo(250));
		});
	}

	[TestCase(AdbGatewayRebootMode.Normal, AdbRebootMode.Normal)]
	[TestCase(AdbGatewayRebootMode.Recovery, AdbRebootMode.Recovery)]
	[TestCase(AdbGatewayRebootMode.Bootloader, AdbRebootMode.Bootloader)]
	public async Task RebootAsync_builds_a_reboot_command_with_the_mapped_mode(AdbGatewayRebootMode gatewayMode,
		AdbRebootMode adbMode)
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.RebootAsync("device-1", gatewayMode, CancellationToken.None);

		var command = SingleCommand<AdbRebootCommand>(manager);
		Assert.Multiple(() =>
		{
			Assert.That(command.Serial, Is.EqualTo("device-1"));
			Assert.That(command.Mode, Is.EqualTo(adbMode));
		});
	}

	[Test]
	public async Task A_null_serial_is_passed_through_as_blank_for_the_manager_to_resolve()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		await gateway.SendKeyAsync(null, AdbGatewayKey.Back, CancellationToken.None);

		Assert.That(SingleCommand<AdbKeyEventCommand>(manager).Serial, Is.Empty);
	}

	[TestCase(AdbFailureCode.Disabled, AdbGatewayFailureCode.Disabled)]
	[TestCase(AdbFailureCode.ExecutableNotFound, AdbGatewayFailureCode.ExecutableNotFound)]
	[TestCase(AdbFailureCode.ServerUnreachable, AdbGatewayFailureCode.ServerUnreachable)]
	[TestCase(AdbFailureCode.DeviceNotFound, AdbGatewayFailureCode.DeviceNotFound)]
	[TestCase(AdbFailureCode.DeviceUnauthorized, AdbGatewayFailureCode.DeviceUnauthorized)]
	[TestCase(AdbFailureCode.DeviceOffline, AdbGatewayFailureCode.DeviceOffline)]
	[TestCase(AdbFailureCode.Timeout, AdbGatewayFailureCode.Timeout)]
	[TestCase(AdbFailureCode.InvalidParameter, AdbGatewayFailureCode.InvalidParameter)]
	[TestCase(AdbFailureCode.CommandFailed, AdbGatewayFailureCode.CommandFailed)]
	[TestCase(AdbFailureCode.Unsupported, AdbGatewayFailureCode.Unsupported)]
	public async Task A_manager_failure_maps_to_the_matching_gateway_failure_code_and_message(
		AdbFailureCode managerCode,
		AdbGatewayFailureCode gatewayCode)
	{
		var manager = EnabledManager();
		manager.ExecuteResult = Result.Fail<AdbFailureCode>(managerCode, "failure detail");
		using var gateway = CreateGateway(manager);

		var result = await gateway.SendKeyAsync("device-1", AdbGatewayKey.Home, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Failure, Is.EqualTo(gatewayCode));
			Assert.That(result.Message, Is.EqualTo("failure detail"));
		});
	}

	[Test]
	public async Task Every_command_method_returns_disabled_without_calling_the_manager_when_status_is_disabled()
	{
		var manager = new FakeAdbManager { Status = AdbStatus.Disabled };
		using var gateway = CreateGateway(manager);

		var keyResult = await gateway.SendKeyAsync(null, AdbGatewayKey.Home, CancellationToken.None);
		var startResult = await gateway.StartAppAsync(null, "pkg", CancellationToken.None);
		var forceStopResult = await gateway.ForceStopAppAsync(null, "pkg", CancellationToken.None);
		var openUriResult = await gateway.OpenUriAsync(null, "uri", CancellationToken.None);
		var inputTextResult = await gateway.InputTextAsync(null, "text", CancellationToken.None);
		var tapResult = await gateway.TapAsync(null, 0, 0, CancellationToken.None);
		var swipeResult = await gateway.SwipeAsync(null, 0, 0, 1, 1, 100, CancellationToken.None);
		var rebootResult = await gateway.RebootAsync(null, AdbGatewayRebootMode.Normal, CancellationToken.None);
		var (screenshotResult, png) = await gateway.CaptureScreenshotAsync(null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			foreach (var result in new[]
				{
					keyResult, startResult, forceStopResult, openUriResult, inputTextResult, tapResult, swipeResult,
					rebootResult, screenshotResult
				})
			{
				Assert.That(result.Success, Is.False);
				Assert.That(result.Failure, Is.EqualTo(AdbGatewayFailureCode.Disabled));
			}

			Assert.That(png, Is.Null);
			Assert.That(manager.ExecutedCommands, Is.Empty);
			Assert.That(manager.CaptureScreenshotSerials, Is.Empty);
		});
	}

	[Test]
	public async Task GetPropertiesAsync_returns_null_without_calling_the_manager_when_status_is_disabled()
	{
		var manager = new FakeAdbManager { Status = AdbStatus.Disabled };
		using var gateway = CreateGateway(manager);

		var properties = await gateway.GetPropertiesAsync(null, CancellationToken.None);

		Assert.That(properties, Is.Null);
		Assert.That(manager.PropertiesSerials, Is.Empty);
	}

	[Test]
	public async Task CaptureScreenshotAsync_returns_the_bytes_unchanged()
	{
		var manager = EnabledManager();
		byte[] png = [1, 2, 3, 4];
		manager.CaptureScreenshotResult = Result.Ok<byte[], AdbFailureCode>(png);
		using var gateway = CreateGateway(manager);

		var (result, bytes) = await gateway.CaptureScreenshotAsync("device-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(bytes, Is.SameAs(png));
			Assert.That(manager.CaptureScreenshotSerials, Has.Count.EqualTo(1));
			Assert.That(manager.CaptureScreenshotSerials[0], Is.EqualTo("device-1"));
		});
	}

	[Test]
	public async Task GetPropertiesAsync_maps_the_manager_properties()
	{
		var manager = EnabledManager();
		manager.PropertiesResult = new AdbDeviceProperties(80, true, false, "com.example.app", DateTimeOffset.UtcNow);
		using var gateway = CreateGateway(manager);

		var properties = await gateway.GetPropertiesAsync("device-1", CancellationToken.None);

		Assert.That(properties, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(properties!.BatteryLevel, Is.EqualTo(80));
			Assert.That(properties.ScreenOn, Is.True);
			Assert.That(properties.Locked, Is.False);
			Assert.That(properties.ForegroundPackage, Is.EqualTo("com.example.app"));
		});
	}

	[Test]
	public void IsEnabled_reflects_the_manager_status()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		Assert.That(gateway.IsEnabled, Is.True);

		manager.Status = AdbStatus.Disabled;

		Assert.That(gateway.IsEnabled, Is.False);
	}

	[Test]
	public void Devices_maps_every_manager_device_including_tunnel_established()
	{
		var manager = EnabledManager();
		manager.Devices =
		[
			new AdbDevice("device-1",
				AdbDeviceState.Device,
				"Pixel",
				"Google",
				"product",
				"t1",
				new AdbTunnel(true, 5555, 5555, null, null),
				DateTimeOffset.UtcNow),
			PlainDevice("device-2", AdbDeviceState.Unauthorized)
		];
		using var gateway = CreateGateway(manager);

		var devices = gateway.Devices;

		Assert.That(devices, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(devices[0].Serial, Is.EqualTo("device-1"));
			Assert.That(devices[0].State, Is.EqualTo(AdbGatewayDeviceState.Device));
			Assert.That(devices[0].Model, Is.EqualTo("Pixel"));
			Assert.That(devices[0].Manufacturer, Is.EqualTo("Google"));
			Assert.That(devices[0].TunnelEstablished, Is.True);
			Assert.That(devices[0].IsAuthorized, Is.True);
			Assert.That(devices[1].Serial, Is.EqualTo("device-2"));
			Assert.That(devices[1].State, Is.EqualTo(AdbGatewayDeviceState.Unauthorized));
			Assert.That(devices[1].TunnelEstablished, Is.False);
			Assert.That(devices[1].IsAuthorized, Is.False);
		});
	}

	[Test]
	public void DefaultDeviceSerial_reflects_the_manager_status()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);

		Assert.That(gateway.DefaultDeviceSerial, Is.EqualTo("device-1"));
	}

	[TestCase(AdbDeviceChangeKind.Connected, AdbGatewayDeviceChangeKind.Connected)]
	[TestCase(AdbDeviceChangeKind.Disconnected, AdbGatewayDeviceChangeKind.Disconnected)]
	[TestCase(AdbDeviceChangeKind.Authorized, AdbGatewayDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceChangeKind.Unauthorized, AdbGatewayDeviceChangeKind.Unauthorized)]
	[TestCase(AdbDeviceChangeKind.Online, AdbGatewayDeviceChangeKind.Online)]
	[TestCase(AdbDeviceChangeKind.Offline, AdbGatewayDeviceChangeKind.Offline)]
	public void A_manager_DeviceChanged_raise_reaches_a_gateway_subscriber_exactly_once_with_the_mapped_kind(
		AdbDeviceChangeKind managerKind,
		AdbGatewayDeviceChangeKind gatewayKind)
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);
		var received = new List<AdbGatewayDeviceChange>();
		gateway.DeviceChanged += (_, change) => received.Add(change);

		var device = PlainDevice("device-2", AdbDeviceState.Unauthorized);
		manager.RaiseDeviceChanged(new AdbDeviceChange(managerKind, device, AdbDeviceState.Device));

		Assert.That(received, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(received[0].Kind, Is.EqualTo(gatewayKind));
			Assert.That(received[0].Device.Serial, Is.EqualTo("device-2"));
			Assert.That(received[0].Device.State, Is.EqualTo(AdbGatewayDeviceState.Unauthorized));
		});
	}

	[Test]
	public void Subscribing_unsubscribing_and_resubscribing_a_handler_yields_exactly_one_delivery_per_event()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);
		var callCount = 0;
		EventHandler<AdbGatewayDeviceChange> handler = (_, _) => callCount++;

		gateway.DeviceChanged += handler;
		gateway.DeviceChanged -= handler;
		gateway.DeviceChanged += handler;

		manager.RaiseDeviceChanged(new AdbDeviceChange(AdbDeviceChangeKind.Connected, PlainDevice("device-1"), null));

		Assert.That(callCount, Is.EqualTo(1));
	}

	[Test]
	public void A_throwing_gateway_subscriber_does_not_propagate_out_of_the_manager_raise()
	{
		var manager = EnabledManager();
		using var gateway = CreateGateway(manager);
		var secondSubscriberCalled = false;
		gateway.DeviceChanged += (_, _) => throw new InvalidOperationException("boom");
		gateway.DeviceChanged += (_, _) => secondSubscriberCalled = true;

		Assert.DoesNotThrow(() =>
			manager.RaiseDeviceChanged(
				new AdbDeviceChange(AdbDeviceChangeKind.Connected, PlainDevice("device-1"), null)));
		Assert.That(secondSubscriberCalled, Is.True);
	}
}
