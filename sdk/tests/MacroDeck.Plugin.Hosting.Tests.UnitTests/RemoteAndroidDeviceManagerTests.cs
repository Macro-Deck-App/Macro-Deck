using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Android;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteAndroidDeviceManagerTests
{
	private RemoteIntegrationContextTests.RecordingHostInvoker _invoker = null!;
	private HostStateCache _cache = null!;
	private RemoteAndroidDeviceManager _manager = null!;
	private List<string> _events = null!;

	[SetUp]
	public void SetUp()
	{
		_invoker = new RemoteIntegrationContextTests.RecordingHostInvoker();
		_cache = new HostStateCache(new PluginConnectionState());
		_manager = new RemoteAndroidDeviceManager(_invoker, _cache, Serilog.Core.Logger.None);
		_events = [];
		_manager.AccessChanged += (_, _) => _events.Add("access");
		_manager.DeviceConnected += (_, e) => _events.Add($"connected:{e.Device.Serial}");
		_manager.DeviceDisconnected += (_, e) => _events.Add($"disconnected:{e.Device.Serial}");
		_manager.DeviceStateChanged += (_, e)
			=> _events.Add($"state:{e.Device.Serial}:{e.PreviousState}->{e.Device.State}");
	}

	[TearDown]
	public void TearDown()
	{
		_invoker.Dispose();
	}

	[Test]
	public void Before_the_host_reports_anything_access_is_unsupported_and_no_device_is_listed()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.Unsupported));
			Assert.That(_manager.Devices, Is.Empty);
		});
	}

	[Test]
	public void A_push_lists_the_devices_with_their_info_and_state_and_reports_each_as_connected()
	{
		Push(1,
			AdbAccessStates.Available,
			Device("A1", AdbDeviceStates.Online, "Pixel 8"),
			Device("B2", AdbDeviceStates.Unauthorized));

		var pixel = _manager.FindDevice("A1")!;
		Assert.Multiple(() =>
		{
			Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.Available));
			Assert.That(_manager.Devices.Select(device => device.Serial), Is.EquivalentTo(new[] { "A1", "B2" }));
			Assert.That(pixel.Info, Is.EqualTo(new AndroidDeviceInfo("Pixel 8", "Google", "shiba")));
			Assert.That(pixel.State, Is.EqualTo(AndroidDeviceState.Online));
			Assert.That(_manager.FindDevice("B2")!.State, Is.EqualTo(AndroidDeviceState.Unauthorized));
			Assert.That(_events, Is.EqualTo(new[] { "access", "connected:A1", "connected:B2" }));
		});
	}

	[Test]
	public void The_stable_id_the_host_reports_is_part_of_the_device_info()
	{
		Push(1,
			AdbAccessStates.Available,
			Device("192.168.1.20:5555", AdbDeviceStates.Online, "Pixel 8") with { StableId = "3fa9c2d1e0b4a756" },
			Device("B2", AdbDeviceStates.Online, "Pixel 7"));

		Assert.Multiple(() =>
		{
			Assert.That(_manager.FindDevice("192.168.1.20:5555")!.Info,
				Is.EqualTo(new AndroidDeviceInfo("Pixel 8", "Google", "shiba") { StableId = "3fa9c2d1e0b4a756" }));
			Assert.That(_manager.FindDevice("B2")!.Info.StableId, Is.Null);
		});
	}

	[Test]
	public void The_integration_context_of_a_built_plugin_hands_out_the_same_manager_as_dependency_injection()
	{
		using var plugin = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.Build();

		Assert.That(plugin.Services.GetRequiredService<IIntegrationContext>().AndroidDevices,
			Is.SameAs(plugin.Services.GetRequiredService<IAndroidDeviceManager>()));
	}

	[Test]
	public void A_state_change_keeps_the_same_device_instance_and_reports_the_previous_state()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Connecting));
		var device = _manager.FindDevice("A1");
		_events.Clear();

		Push(2, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));

		Assert.Multiple(() =>
		{
			Assert.That(_manager.FindDevice("A1"), Is.SameAs(device));
			Assert.That(_events, Is.EqualTo(new[] { "state:A1:Connecting->Online" }));
		});
	}

	[Test]
	public void A_device_that_leaves_and_returns_is_reported_both_ways_and_stays_the_same_instance()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		var device = _manager.FindDevice("A1");
		_events.Clear();

		Push(2, AdbAccessStates.Available);
		var whileGone = _manager.FindDevice("A1");
		Push(3, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));

		Assert.Multiple(() =>
		{
			Assert.That(whileGone, Is.Null);
			Assert.That(_manager.FindDevice("A1"), Is.SameAs(device));
			Assert.That(_events, Is.EqualTo(new[] { "disconnected:A1", "connected:A1" }));
		});
	}

	[Test]
	public void Withdrawn_access_hides_every_device_even_if_the_push_lists_some()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_events.Clear();

		Push(2, AdbAccessStates.NotAllowed, Device("A1", AdbDeviceStates.Online));

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.AdbNotAllowed));
			Assert.That(_manager.Devices, Is.Empty);
			Assert.That(_events, Is.EqualTo(new[] { "access", "disconnected:A1" }));
		});
	}

	[Test]
	public void A_push_older_than_the_last_one_applied_is_ignored()
	{
		Push(5, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));

		Push(4, AdbAccessStates.NotEnabled);

		Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.Available));
	}

	[Test]
	public void A_throwing_handler_does_not_stop_the_other_changes_from_being_reported()
	{
		_manager.DeviceConnected += (_, e) =>
		{
			if (e.Device.Serial == "A1")
			{
				throw new InvalidOperationException("plugin bug");
			}
		};

		Assert.DoesNotThrow(() => Push(1,
			AdbAccessStates.Available,
			Device("A1", AdbDeviceStates.Online),
			Device("B2", AdbDeviceStates.Online)));
		Assert.That(_events, Does.Contain("connected:B2"));
	}

	[Test]
	public async Task A_shell_call_sends_the_adb_shell_operation_for_this_device_and_returns_the_result()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = Json(new AdbShellResultDto
			{ ExitCode = 3, StandardOutput = "out", StandardError = "err", Truncated = true });

		var result = await _manager.FindDevice("A1")!.ExecuteShellAsync("getprop ro.build.version.sdk");

		var arguments = (AdbShellArguments)_invoker.LastArguments!;
		Assert.Multiple(() =>
		{
			Assert.That((_invoker.LastApi, _invoker.LastOperation),
				Is.EqualTo((HostApis.Adb, HostOperations.Adb.Shell)));
			Assert.That((arguments.Serial, arguments.Command), Is.EqualTo(("A1", "getprop ro.build.version.sdk")));
			Assert.That(result, Is.EqualTo(new AndroidShellResult(3, "out", "err", true)));
		});
	}

	[Test]
	public async Task The_battery_state_is_mapped_from_the_wire_strings()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = Json(new AdbBatteryStateDto
		{
			Level = 87, IsCharging = true, Status = AdbBatteryStatuses.Full, Health = AdbBatteryHealths.Overheat
		});

		var battery = await _manager.FindDevice("A1")!.GetBatteryStateAsync();

		Assert.That(battery,
			Is.EqualTo(new AndroidBatteryState
			{
				Level = 87, IsCharging = true, Status = AndroidBatteryStatus.Full,
				Health = AndroidBatteryHealth.Overheat
			}));
	}

	[Test]
	public async Task A_battery_status_this_sdk_does_not_know_reads_as_unknown()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = Json(new AdbBatteryStateDto { Level = 10, Status = "quantum", Health = "radiant" });

		var battery = await _manager.FindDevice("A1")!.GetBatteryStateAsync();

		Assert.That((battery.Status, battery.Health),
			Is.EqualTo((AndroidBatteryStatus.Unknown, AndroidBatteryHealth.Unknown)));
	}

	private static IEnumerable<TestCaseData> Operations()
	{
		yield return Case(HostOperations.Adb.Push, device => device.PushFileAsync(@"C:\data\a.txt", "/sdcard/a.txt"));
		yield return Case(HostOperations.Adb.Pull, device => device.PullFileAsync("/sdcard/a.txt", @"C:\data\a.txt"));
		yield return Case(HostOperations.Adb.Install, device => device.InstallApkAsync(@"C:\data\app.apk"));
		yield return Case(HostOperations.Adb.Uninstall, device => device.UninstallPackageAsync("com.example.app"));
		yield return Case(HostOperations.Adb.PackageInstalled,
			device => device.IsPackageInstalledAsync("com.example.app"),
			new AdbPackageInstalledDto { Installed = true });
	}

	private static TestCaseData Case(string operation, Func<IAndroidDevice, Task> exercise, object? result = null)
	{
		return new TestCaseData(operation, exercise, result).SetName($"{{m}}({operation})");
	}

	[TestCaseSource(nameof(Operations))]
	public async Task Every_device_operation_sends_its_declared_adb_operation(
		string operation,
		Func<IAndroidDevice, Task> exercise,
		object? result)
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = result is null ? null : Json(result);

		await exercise(_manager.FindDevice("A1")!);

		Assert.That((_invoker.LastApi, _invoker.LastOperation), Is.EqualTo((HostApis.Adb, operation)));
	}

	private static IEnumerable<TestCaseData> Failures()
	{
		yield return new TestCaseData(ProtocolErrorCodes.AdbNotEnabled, null, AndroidDeviceErrorCode.AdbNotEnabled);
		yield return new TestCaseData(ProtocolErrorCodes.AdbNotAllowed, null, AndroidDeviceErrorCode.AdbNotAllowed);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbDeviceOffline,
			AndroidDeviceErrorCode.DeviceOffline);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbDeviceNotFound,
			AndroidDeviceErrorCode.DeviceNotFound);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbDeviceUnauthorized,
			AndroidDeviceErrorCode.DeviceUnauthorized);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbExecutableNotFound,
			AndroidDeviceErrorCode.AdbUnavailable);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbCommandFailed,
			AndroidDeviceErrorCode.CommandFailed);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			ProtocolErrorReasons.AdbInvalidArgument,
			AndroidDeviceErrorCode.InvalidArgument);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed,
			"a_reason_from_the_future",
			AndroidDeviceErrorCode.Unknown);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnavailable,
			ProtocolErrorReasons.HostLocked,
			AndroidDeviceErrorCode.HostLocked);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnavailable,
			null,
			AndroidDeviceErrorCode.HostUnavailable);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnsupported,
			null,
			AndroidDeviceErrorCode.Unsupported);
		yield return new TestCaseData(ProtocolErrorCodes.RateLimited, null, AndroidDeviceErrorCode.RateLimited);
		yield return new TestCaseData(ProtocolErrorCodes.Timeout, null, AndroidDeviceErrorCode.Timeout);
	}

	[TestCaseSource(nameof(Failures))]
	public void A_host_error_surfaces_as_an_AndroidDeviceException_with_a_distinct_code(
		string code,
		string? reason,
		AndroidDeviceErrorCode expected)
	{
		var error = new ProtocolError
		{
			Code = code,
			Message = "refused",
			Retryable = false,
			Details = reason is null ? null : new Dictionary<string, string> { ["reason"] = reason }
		};
		var device = new RemoteAndroidDevice("A1", new FailingHostInvoker(error));

		var exception = Assert.ThrowsAsync<AndroidDeviceException>(() => device.ExecuteShellAsync("true"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(expected));
	}

	[Test]
	public void A_cancelled_host_call_surfaces_as_cancellation_not_as_a_device_error()
	{
		var device = new RemoteAndroidDevice("A1",
			new FailingHostInvoker(new ProtocolError
			{
				Code = ProtocolErrorCodes.Cancelled, Message = "cancelled", Retryable = false
			}));

		Assert.That(() => device.InstallApkAsync(@"C:\app.apk"), Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task Connecting_asks_the_host_and_returns_the_device_the_following_push_brings_online()
	{
		Push(1, AdbAccessStates.Available);
		_invoker.NextResult = Json(new AdbConnectResultDto { Serial = "192.168.1.20:5555" });

		var connected = await _manager.ConnectAsync("192.168.1.20:5555");
		Push(2, AdbAccessStates.Available, Device("192.168.1.20:5555", AdbDeviceStates.Online));

		Assert.Multiple(() =>
		{
			Assert.That((_invoker.LastApi, _invoker.LastOperation),
				Is.EqualTo((HostApis.Adb, HostOperations.Adb.Connect)));
			Assert.That(((AdbConnectArguments)_invoker.LastArguments!).Address, Is.EqualTo("192.168.1.20:5555"));
			Assert.That(_manager.FindDevice("192.168.1.20:5555"), Is.SameAs(connected));
			Assert.That(connected.State, Is.EqualTo(AndroidDeviceState.Online));
		});
	}

	[Test]
	public void A_connect_the_host_could_not_complete_surfaces_as_a_command_failure()
	{
		var manager = new RemoteAndroidDeviceManager(new FailingHostInvoker(new ProtocolError
			{
				Code = ProtocolErrorCodes.AdbFailed,
				Message = "failed to connect to '192.168.1.20:5555': Connection refused",
				Retryable = false,
				Details = new Dictionary<string, string> { ["reason"] = ProtocolErrorReasons.AdbCommandFailed }
			}),
			_cache,
			Serilog.Core.Logger.None);

		var exception = Assert.ThrowsAsync<AndroidDeviceException>(() => manager.ConnectAsync("192.168.1.20:5555"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.CommandFailed));
	}

	private static IEnumerable<TestCaseData> TypedOperations()
	{
		yield return Typed(HostOperations.Adb.Key,
			device => device.SendKeyAsync(AndroidKey.VolumeUp),
			new AdbKeyArguments { Serial = "A1", Key = AdbKeys.VolumeUp });
		yield return Typed(HostOperations.Adb.Tap,
			device => device.TapAsync(10, 20),
			new AdbTapArguments { Serial = "A1", X = 10, Y = 20 });
		yield return Typed(HostOperations.Adb.Swipe,
			device => device.SwipeAsync(1, 2, 3, 4, TimeSpan.FromMilliseconds(250)),
			new AdbSwipeArguments
				{ Serial = "A1", FromX = 1, FromY = 2, ToX = 3, ToY = 4, DurationMilliseconds = 250 });
		yield return Typed(HostOperations.Adb.InputText,
			device => device.InputTextAsync("hello"),
			new AdbTextArguments { Serial = "A1", Text = "hello" });
		yield return Typed(HostOperations.Adb.StartApp,
			device => device.StartAppAsync("com.example.app"),
			new AdbPackageArguments { Serial = "A1", PackageName = "com.example.app" });
		yield return Typed(HostOperations.Adb.ForceStopApp,
			device => device.ForceStopAppAsync("com.example.app"),
			new AdbPackageArguments { Serial = "A1", PackageName = "com.example.app" });
		yield return Typed(HostOperations.Adb.OpenUri,
			device => device.OpenUriAsync("https://example.com"),
			new AdbUriArguments { Serial = "A1", Uri = "https://example.com" });
		yield return Typed(HostOperations.Adb.Reboot,
			device => device.RebootAsync(AndroidRebootMode.Bootloader),
			new AdbRebootArguments { Serial = "A1", Mode = AdbRebootModes.Bootloader });
	}

	private static TestCaseData Typed(string operation, Func<IAndroidDevice, Task> exercise, object arguments)
	{
		return new TestCaseData(operation, exercise, arguments).SetName($"{{m}}({operation})");
	}

	[TestCaseSource(nameof(TypedOperations))]
	public async Task Every_typed_operation_sends_its_adb_operation_and_arguments(
		string operation,
		Func<IAndroidDevice, Task> exercise,
		object arguments)
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));

		await exercise(_manager.FindDevice("A1")!);

		Assert.Multiple(() =>
		{
			Assert.That((_invoker.LastApi, _invoker.LastOperation), Is.EqualTo((HostApis.Adb, operation)));
			Assert.That(_invoker.LastArguments, Is.EqualTo(arguments));
		});
	}

	[Test]
	public void A_typed_operation_against_a_host_that_predates_it_is_unsupported()
	{
		var device = new RemoteAndroidDevice("A1",
			new FailingHostInvoker(new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnsupported, Message = "no operation 'key'", Retryable = false
			}));

		var exception = Assert.ThrowsAsync<AndroidDeviceException>(() => device.SendKeyAsync(AndroidKey.Home));

		Assert.That(exception!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.Unsupported));
	}

	[Test]
	public async Task The_status_is_read_from_the_host_and_null_while_it_has_no_reading()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		var device = _manager.FindDevice("A1")!;

		var before = await device.GetStatusAsync();
		_invoker.NextResult = Json(new AdbDeviceStatusDto
			{ ScreenOn = true, Locked = false, ForegroundPackage = "com.example.app" });
		var after = await device.GetStatusAsync();

		Assert.Multiple(() =>
		{
			Assert.That((_invoker.LastApi, _invoker.LastOperation),
				Is.EqualTo((HostApis.Adb, HostOperations.Adb.Status)));
			Assert.That(before, Is.Null);
			Assert.That(after,
				Is.EqualTo(new AndroidDeviceStatus
					{ IsScreenOn = true, IsLocked = false, ForegroundPackage = "com.example.app" }));
		});
	}

	[Test]
	public async Task The_status_from_a_host_that_predates_it_is_null()
	{
		var device = new RemoteAndroidDevice("A1",
			new FailingHostInvoker(new ProtocolError
			{
				Code = ProtocolErrorCodes.CapabilityUnsupported, Message = "no operation 'status'", Retryable = false
			}));

		Assert.That(await device.GetStatusAsync(), Is.Null);
	}

	[Test]
	public async Task A_screenshot_arrives_over_the_host_asset_channel_named_in_the_reply()
	{
		var assets = new ScriptedHostAssets();
		var manager = new RemoteAndroidDeviceManager(_invoker, _cache, Serilog.Core.Logger.None, assets);
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		byte[] png = [137, 80, 78, 71];
		assets.Transfers["transfer-1"] = new HostAssetTransfer(true, "image/png", png);
		_invoker.NextResult = Json(new AdbScreenshotResultDto
		{
			ByteLength = png.Length, ContentHash = "sha256:x", TransferId = "transfer-1"
		});

		var bytes = await manager.FindDevice("A1")!.CaptureScreenshotAsync();

		Assert.Multiple(() =>
		{
			Assert.That(bytes, Is.EqualTo(png));
			Assert.That(_invoker.LastOperation, Is.EqualTo(HostOperations.Adb.Screenshot));
		});
	}

	[Test]
	public void A_device_lacking_usb_permission_reads_unauthorized_and_a_change_of_it_is_reported()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Unauthorized));
		_events.Clear();

		Push(2,
			AdbAccessStates.Available,
			Device("A1", AdbDeviceStates.Unauthorized) with { LacksUsbPermission = true });

		var device = _manager.FindDevice("A1")!;
		Assert.Multiple(() =>
		{
			Assert.That(device.State, Is.EqualTo(AndroidDeviceState.Unauthorized));
			Assert.That(device.LacksUsbPermission, Is.True);
			Assert.That(_events, Is.EqualTo(new[] { "state:A1:Unauthorized->Unauthorized" }));
		});
	}

	[Test]
	public void An_offline_device_carries_the_mode_the_host_reports_and_a_mode_change_is_reported()
	{
		Push(1,
			AdbAccessStates.Available,
			Device("A1", AdbDeviceStates.Offline),
			Device("B2", AdbDeviceStates.Offline) with { Mode = "warp" });
		_events.Clear();

		Push(2,
			AdbAccessStates.Available,
			Device("A1", AdbDeviceStates.Offline) with { Mode = AdbDeviceModes.Recovery },
			Device("B2", AdbDeviceStates.Offline) with { Mode = "warp" });

		Assert.Multiple(() =>
		{
			Assert.That(_manager.FindDevice("A1")!.Mode, Is.EqualTo(AndroidDeviceMode.Recovery));
			Assert.That(_manager.FindDevice("B2")!.Mode, Is.EqualTo(AndroidDeviceMode.Unknown));
			Assert.That(_events, Is.EqualTo(new[] { "state:A1:Offline->Offline" }));
		});
	}

	[Test]
	public void A_missing_adb_is_reported_from_the_push_and_absent_from_an_older_host()
	{
		Push(1, AdbAccessStates.Available);
		var older = _manager.IsAdbUnavailable;
		_events.Clear();

		_cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.Adb,
					Data = JsonSerializer.SerializeToElement(new AdbStateDto
						{
							Access = AdbAccessStates.Available, AdbUnavailable = true, Revision = 2
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});

		Assert.Multiple(() =>
		{
			Assert.That(older, Is.False);
			Assert.That(_manager.IsAdbUnavailable, Is.True);
			Assert.That(_events, Is.EqualTo(new[] { "access" }));
		});
	}

	[Test]
	public void The_default_device_is_the_one_the_host_names_and_none_from_a_host_that_names_none()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online), Device("B2", AdbDeviceStates.Online));
		var none = _manager.DefaultDevice;

		_cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.Adb,
					Data = JsonSerializer.SerializeToElement(new AdbStateDto
						{
							Access = AdbAccessStates.Available,
							Devices = [Device("A1", AdbDeviceStates.Online)],
							DefaultDeviceSerial = "B2",
							Revision = 2
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});

		Assert.Multiple(() =>
		{
			Assert.That(none, Is.Null);
			Assert.That(_manager.DefaultDevice?.Serial, Is.EqualTo("B2"));
			Assert.That(_manager.FindDevice("B2"), Is.Null, "the default device can be one that is not attached");
		});
	}

	private void Push(long revision, string access, params AdbDeviceStateDto[] devices)
	{
		_cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.Adb,
					Data = JsonSerializer.SerializeToElement(new AdbStateDto
						{
							Access = access, Devices = devices, Revision = revision
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});
	}

	private static AdbDeviceStateDto Device(string serial, string state, string? model = null)
	{
		return new AdbDeviceStateDto
		{
			Serial = serial,
			State = state,
			Model = model,
			Manufacturer = model is null ? null : "Google",
			Product = model is null ? null : "shiba"
		};
	}

	private static JsonElement Json(object value)
	{
		return JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
	}

	private sealed class FailingHostInvoker(ProtocolError error) : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			return Task.FromException<JsonElement?>(HostInvocationException.From(error));
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}

	private sealed class ScriptedHostAssets : IPluginHostAssetReceiver
	{
		public Dictionary<string, HostAssetTransfer> Transfers { get; } = new(StringComparer.Ordinal);

		public Task<HostAssetTransfer> AwaitAsync(string transferId, CancellationToken cancellationToken)
		{
			return Task.FromResult(Transfers[transferId]);
		}

		public ProtocolEnvelope Handle(ProtocolEnvelope envelope)
		{
			throw new NotSupportedException();
		}

		public void Reset()
		{
		}
	}
}
