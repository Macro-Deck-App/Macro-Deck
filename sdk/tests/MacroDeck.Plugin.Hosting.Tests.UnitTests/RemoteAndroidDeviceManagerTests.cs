using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Android;

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
		_manager.DeviceStateChanged += (_, e) => _events.Add($"state:{e.Device.Serial}:{e.PreviousState}->{e.Device.State}");
	}

	[TearDown]
	public void TearDown() => _invoker.Dispose();

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
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online, "Pixel 8"), Device("B2", AdbDeviceStates.Unauthorized));

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

		Assert.DoesNotThrow(() => Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online), Device("B2", AdbDeviceStates.Online)));
		Assert.That(_events, Does.Contain("connected:B2"));
	}

	[Test]
	public async Task A_shell_call_sends_the_adb_shell_operation_for_this_device_and_returns_the_result()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = Json(new AdbShellResultDto { ExitCode = 3, StandardOutput = "out", StandardError = "err", Truncated = true });

		var result = await _manager.FindDevice("A1")!.ExecuteShellAsync("getprop ro.build.version.sdk");

		var arguments = (AdbShellArguments)_invoker.LastArguments!;
		Assert.Multiple(() =>
		{
			Assert.That((_invoker.LastApi, _invoker.LastOperation), Is.EqualTo((HostApis.Adb, HostOperations.Adb.Shell)));
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

		Assert.That(battery, Is.EqualTo(new AndroidBatteryState
		{
			Level = 87, IsCharging = true, Status = AndroidBatteryStatus.Full, Health = AndroidBatteryHealth.Overheat
		}));
	}

	[Test]
	public async Task A_battery_status_this_sdk_does_not_know_reads_as_unknown()
	{
		Push(1, AdbAccessStates.Available, Device("A1", AdbDeviceStates.Online));
		_invoker.NextResult = Json(new AdbBatteryStateDto { Level = 10, Status = "quantum", Health = "radiant" });

		var battery = await _manager.FindDevice("A1")!.GetBatteryStateAsync();

		Assert.That((battery.Status, battery.Health), Is.EqualTo((AndroidBatteryStatus.Unknown, AndroidBatteryHealth.Unknown)));
	}

	private static IEnumerable<TestCaseData> Operations()
	{
		yield return Case(HostOperations.Adb.Push, device => device.PushFileAsync(@"C:\data\a.txt", "/sdcard/a.txt"));
		yield return Case(HostOperations.Adb.Pull, device => device.PullFileAsync("/sdcard/a.txt", @"C:\data\a.txt"));
		yield return Case(HostOperations.Adb.Install, device => device.InstallApkAsync(@"C:\data\app.apk"));
		yield return Case(HostOperations.Adb.Uninstall, device => device.UninstallPackageAsync("com.example.app"));
		yield return Case(HostOperations.Adb.PackageInstalled, device => device.IsPackageInstalledAsync("com.example.app"),
			new AdbPackageInstalledDto { Installed = true });
	}

	private static TestCaseData Case(string operation, Func<IAndroidDevice, Task> exercise, object? result = null)
		=> new TestCaseData(operation, exercise, result).SetName($"{{m}}({operation})");

	[TestCaseSource(nameof(Operations))]
	public async Task Every_device_operation_sends_its_declared_adb_operation(string operation,
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
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbDeviceOffline, AndroidDeviceErrorCode.DeviceOffline);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbDeviceNotFound, AndroidDeviceErrorCode.DeviceNotFound);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbDeviceUnauthorized, AndroidDeviceErrorCode.DeviceUnauthorized);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbExecutableNotFound, AndroidDeviceErrorCode.AdbUnavailable);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbCommandFailed, AndroidDeviceErrorCode.CommandFailed);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, ProtocolErrorReasons.AdbInvalidArgument, AndroidDeviceErrorCode.InvalidArgument);
		yield return new TestCaseData(ProtocolErrorCodes.AdbFailed, "a_reason_from_the_future", AndroidDeviceErrorCode.Unknown);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnavailable, ProtocolErrorReasons.HostLocked, AndroidDeviceErrorCode.HostLocked);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnavailable, null, AndroidDeviceErrorCode.HostUnavailable);
		yield return new TestCaseData(ProtocolErrorCodes.CapabilityUnsupported, null, AndroidDeviceErrorCode.Unsupported);
		yield return new TestCaseData(ProtocolErrorCodes.RateLimited, null, AndroidDeviceErrorCode.RateLimited);
		yield return new TestCaseData(ProtocolErrorCodes.Timeout, null, AndroidDeviceErrorCode.Timeout);
	}

	[TestCaseSource(nameof(Failures))]
	public void A_host_error_surfaces_as_an_AndroidDeviceException_with_a_distinct_code(string code,
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
		var device = new RemoteAndroidDevice("A1", new FailingHostInvoker(new ProtocolError
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
			Assert.That((_invoker.LastApi, _invoker.LastOperation), Is.EqualTo((HostApis.Adb, HostOperations.Adb.Connect)));
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

	private void Push(long revision, string access, params AdbDeviceStateDto[] devices)
		=> _cache.Apply(new ProtocolEnvelope
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

	private static AdbDeviceStateDto Device(string serial, string state, string? model = null)
		=> new()
		{
			Serial = serial,
			State = state,
			Model = model,
			Manufacturer = model is null ? null : "Google",
			Product = model is null ? null : "shiba"
		};

	private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);

	private sealed class FailingHostInvoker(ProtocolError error) : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
			=> Task.FromException<JsonElement?>(HostInvocationException.From(error));

		public bool TryComplete(ProtocolEnvelope result) => false;
	}
}
