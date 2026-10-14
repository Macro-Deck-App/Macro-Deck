using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAdbCallbacksTests
{
	private const string PluginId = "com.example.android";

	private static IEnumerable<TestCaseData> EveryOperation()
	{
		return HostOperations.Adb.All.Select(operation => new TestCaseData(operation).SetName($"{{m}}({operation})"));
	}

	[TestCaseSource(nameof(EveryOperation))]
	public async Task Every_operation_is_refused_as_not_enabled_while_adb_is_off(string operation)
	{
		var callbacks = Callbacks(PluginAdbAccess.NotEnabled);

		var refusal = await callbacks.AdmitAsync(PluginId, Payload(operation), CancellationToken.None);

		Assert.That(refusal?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.AdbNotEnabled));
	}

	[TestCaseSource(nameof(EveryOperation))]
	public async Task Every_operation_is_refused_as_not_allowed_for_a_plugin_without_access(string operation)
	{
		var callbacks = Callbacks(PluginAdbAccess.NotAllowed);

		var refusal = await callbacks.AdmitAsync(PluginId, Payload(operation), CancellationToken.None);

		Assert.That(refusal?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.AdbNotAllowed));
	}

	[TestCase("shell", true)]
	[TestCase("push", true)]
	[TestCase("pull", true)]
	[TestCase("install", true)]
	[TestCase("uninstall", true)]
	[TestCase("connect", true)]
	[TestCase("battery", false)]
	[TestCase("package-installed", false)]
	[TestCase("key", false)]
	[TestCase("tap", false)]
	[TestCase("swipe", false)]
	[TestCase("input-text", false)]
	[TestCase("start-app", false)]
	[TestCase("force-stop-app", false)]
	[TestCase("open-uri", false)]
	[TestCase("reboot", false)]
	[TestCase("screenshot", false)]
	[TestCase("status", false)]
	public async Task While_the_host_is_locked_only_the_open_ended_operations_are_refused(
		string operation,
		bool refused)
	{
		var callbacks = Callbacks(PluginAdbAccess.Available, true);

		var refusal = await callbacks.AdmitAsync(PluginId, Payload(operation), CancellationToken.None);

		Assert.That(refusal?.Error?.Details?["reason"],
			refused ? Is.EqualTo(ProtocolErrorReasons.HostLocked) : Is.Null);
	}

	[TestCase(AdbFailureCode.DeviceOffline, ProtocolErrorReasons.AdbDeviceOffline)]
	[TestCase(AdbFailureCode.DeviceNotFound, ProtocolErrorReasons.AdbDeviceNotFound)]
	[TestCase(AdbFailureCode.DeviceUnauthorized, ProtocolErrorReasons.AdbDeviceUnauthorized)]
	[TestCase(AdbFailureCode.ExecutableNotFound, ProtocolErrorReasons.AdbExecutableNotFound)]
	[TestCase(AdbFailureCode.Timeout, ProtocolErrorReasons.AdbTimeout)]
	[TestCase(AdbFailureCode.InvalidParameter, ProtocolErrorReasons.AdbInvalidArgument)]
	[TestCase(AdbFailureCode.CommandFailed, ProtocolErrorReasons.AdbCommandFailed)]
	public async Task A_device_or_adb_failure_is_ADB_FAILED_with_a_reason_naming_it(
		AdbFailureCode failure,
		string reason)
	{
		var operations = new FakeAdbDeviceOperations
		{
			Install = Result.Fail<AdbFailureCode>(failure, "adb said no")
		};

		var result = await new PluginAdbCallbacks(operations,
				new FixedAdbAccessPolicy(PluginAdbAccess.Available),
				new FakeHostLockState())
			.ExecuteAsync(Payload(HostOperations.Adb.Install), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.AdbFailed));
			Assert.That(result.Error?.Details?["reason"], Is.EqualTo(reason));
		});
	}

	[Test]
	public async Task Adb_switched_off_between_admission_and_execution_is_still_not_enabled()
	{
		var operations = new FakeAdbDeviceOperations
			{ Install = Result.Fail<AdbFailureCode>(AdbFailureCode.Disabled, "off") };

		var result = await new PluginAdbCallbacks(operations,
				new FixedAdbAccessPolicy(PluginAdbAccess.Available),
				new FakeHostLockState())
			.ExecuteAsync(Payload(HostOperations.Adb.Install), CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.AdbNotEnabled));
	}

	[TestCase(PluginAdbAccess.NotEnabled, true, true)]
	[TestCase(PluginAdbAccess.NotAllowed, true, true)]
	[TestCase(PluginAdbAccess.NotAllowed, false, false)]
	[TestCase(PluginAdbAccess.Available, true, false)]
	public async Task A_refused_call_asks_the_user_only_when_allowing_adb_would_let_the_plugin_in(
		PluginAdbAccess access,
		bool grantable,
		bool asked)
	{
		var consent = new RecordingConsentNotifier();
		var callbacks = new PluginAdbCallbacks(new FakeAdbDeviceOperations(),
			new FixedAdbAccessPolicy(access) { Grantable = grantable },
			new FakeHostLockState(),
			consent);

		await callbacks.AdmitAsync(PluginId, Payload(HostOperations.Adb.Battery), CancellationToken.None);

		Assert.That(consent.Asked, asked ? Is.EqualTo(new[] { PluginId }) : Is.Empty);
	}

	[Test]
	public async Task Connecting_answers_with_the_serial_the_device_has_from_then_on()
	{
		var result = await Callbacks(PluginAdbAccess.Available)
			.ExecuteAsync(Payload(HostOperations.Adb.Connect), CancellationToken.None);

		Assert.That(result.Data?.Deserialize<AdbConnectResultDto>(PluginProtocolJson.Options)?.Serial,
			Is.EqualTo("192.168.1.20:5555"));
	}

	[Test]
	public async Task Missing_arguments_are_an_invalid_payload()
	{
		var result = await Callbacks(PluginAdbAccess.Available)
			.ExecuteAsync(new HostInvokePayload { Api = HostApis.Adb, Operation = HostOperations.Adb.Push },
				CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public void Shell_output_that_would_not_fit_one_protocol_message_is_cut_and_marked()
	{
		var dto = PluginAdbCallbacks.FitToBudget(new AdbShellOutput(0,
			new string('é', 90_000),
			new string('', 40_000),
			false));

		var size = JsonSerializer.SerializeToUtf8Bytes(dto, PluginProtocolJson.Options).Length;
		Assert.Multiple(() =>
		{
			Assert.That(dto.Truncated, Is.True);
			Assert.That(size, Is.LessThanOrEqualTo(PluginAdbCallbacks.ShellResultBudgetBytes));
			Assert.That(PluginAdbCallbacks.ShellResultBudgetBytes, Is.LessThan(ProtocolLimits.MaxMessageBytes));
		});
	}

	[Test]
	public void Shell_output_that_fits_is_returned_unchanged()
	{
		var dto = PluginAdbCallbacks.FitToBudget(new AdbShellOutput(1, "out", "err", false));

		Assert.That((dto.ExitCode, dto.StandardOutput, dto.StandardError, dto.Truncated),
			Is.EqualTo((1, "out", "err", false)));
	}

	[Test]
	public async Task The_typed_operations_run_their_fixed_commands_on_the_named_device()
	{
		var adb = new FakeAdbManager();
		var callbacks = TypedCallbacks(adb);

		foreach (var operation in new[]
			{
				HostOperations.Adb.Key, HostOperations.Adb.Tap, HostOperations.Adb.Swipe, HostOperations.Adb.InputText,
				HostOperations.Adb.StartApp, HostOperations.Adb.ForceStopApp, HostOperations.Adb.OpenUri,
				HostOperations.Adb.Reboot
			})
		{
			var result = await callbacks.ExecuteAsync(PluginId, Payload(operation), CancellationToken.None);
			Assert.That(result.Error, Is.Null, operation);
		}

		Assert.That(adb.ExecutedCommands,
			Is.EqualTo(new AdbCommand[]
			{
				new AdbKeyEventCommand("A1", AdbKey.VolumeUp),
				new AdbTapCommand("A1", 10, 20),
				new AdbSwipeCommand("A1", 1, 2, 3, 4, 250),
				new AdbInputTextCommand("A1", "hello"),
				new AdbStartAppCommand("A1", "com.example.app"),
				new AdbForceStopAppCommand("A1", "com.example.app"),
				new AdbOpenUriCommand("A1", "https://example.com"),
				new AdbRebootCommand("A1", AdbRebootMode.Recovery)
			}));
	}

	[Test]
	public async Task A_typed_operation_without_a_serial_is_refused_instead_of_picking_the_default_device()
	{
		var adb = new FakeAdbManager();

		var result = await TypedCallbacks(adb)
			.ExecuteAsync(PluginId,
				new HostInvokePayload
				{
					Api = HostApis.Adb,
					Operation = HostOperations.Adb.Key,
					Arguments = JsonSerializer.SerializeToElement(
						new AdbKeyArguments { Serial = "", Key = AdbKeys.Home },
						PluginProtocolJson.Options)
				},
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.AdbInvalidArgument));
			Assert.That(adb.ExecutedCommands, Is.Empty);
		});
	}

	[Test]
	public async Task A_key_this_host_does_not_know_is_an_invalid_argument()
	{
		var adb = new FakeAdbManager();

		var result = await TypedCallbacks(adb)
			.ExecuteAsync(PluginId,
				new HostInvokePayload
				{
					Api = HostApis.Adb,
					Operation = HostOperations.Adb.Key,
					Arguments = JsonSerializer.SerializeToElement(
						new AdbKeyArguments { Serial = "A1", Key = "quantum" },
						PluginProtocolJson.Options)
				},
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.AdbInvalidArgument));
			Assert.That(adb.ExecutedCommands, Is.Empty);
		});
	}

	[Test]
	public async Task The_status_answers_the_latest_reading_and_no_data_before_the_first()
	{
		var adb = new FakeAdbManager();
		var callbacks = TypedCallbacks(adb);

		var before = await callbacks.ExecuteAsync(PluginId, Payload(HostOperations.Adb.Status), CancellationToken.None);
		adb.PropertiesResult = new AdbDeviceProperties(80, true, true, "com.example.app", DateTimeOffset.UtcNow);
		var after = await callbacks.ExecuteAsync(PluginId, Payload(HostOperations.Adb.Status), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(before.Error, Is.Null);
			Assert.That(before.Data, Is.Null);
			Assert.That(after.Data?.Deserialize<AdbDeviceStatusDto>(PluginProtocolJson.Options),
				Is.EqualTo(new AdbDeviceStatusDto
					{ ScreenOn = true, Locked = true, ForegroundPackage = "com.example.app" }));
			Assert.That(adb.PropertiesSerials, Is.EqualTo(new[] { "A1", "A1" }));
		});
	}

	[Test]
	public async Task A_screenshot_is_announced_in_the_reply_and_sent_over_the_host_asset_channel()
	{
		byte[] png = [137, 80, 78, 71];
		var adb = new FakeAdbManager { CaptureScreenshotResult = Result.Ok<byte[], AdbFailureCode>(png) };
		var assets = new RecordingHostAssetSender();

		var result = await TypedCallbacks(adb, assets)
			.ExecuteAsync(PluginId, Payload(HostOperations.Adb.Screenshot), CancellationToken.None);
		var announced = result.Data?.Deserialize<AdbScreenshotResultDto>(PluginProtocolJson.Options);
		var sent = await assets.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(announced?.ByteLength, Is.EqualTo(png.Length));
			Assert.That(announced?.ContentHash,
				Is.EqualTo(MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(png)));
			Assert.That(sent,
				Is.EqualTo((PluginId, announced?.TransferId,
					MacroDeck.Plugin.Protocol.Assets.AssetKinds.AndroidScreenshot)));
			Assert.That(adb.CaptureScreenshotSerials, Is.EqualTo(new[] { "A1" }));
		});
	}

	[Test]
	public async Task A_host_without_the_adb_manager_does_not_offer_the_typed_operations()
	{
		var result = await Callbacks(PluginAdbAccess.Available)
			.ExecuteAsync(PluginId, Payload(HostOperations.Adb.Key), CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	private static PluginAdbCallbacks TypedCallbacks(FakeAdbManager adb, IPluginHostAssetSender? assets = null)
	{
		return new PluginAdbCallbacks(new FakeAdbDeviceOperations(),
			new FixedAdbAccessPolicy(PluginAdbAccess.Available),
			new FakeHostLockState(),
			adbManager: adb,
			assets: assets);
	}

	private sealed class RecordingHostAssetSender : IPluginHostAssetSender
	{
		public TaskCompletionSource<(string PluginId, string? TransferId, string Kind)> Sent { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<bool> SendAsync(
			string pluginId,
			string transferId,
			string kind,
			string mimeType,
			ReadOnlyMemory<byte> content,
			string contentHash,
			CancellationToken cancellationToken)
		{
			Sent.TrySetResult((pluginId, transferId, kind));
			return Task.FromResult(true);
		}

		public bool TryComplete(string pluginId, MacroDeck.Plugin.Protocol.Envelope.ProtocolEnvelope ack)
		{
			return false;
		}

		public void DropSession(string pluginId)
		{
		}
	}

	private static PluginAdbCallbacks Callbacks(PluginAdbAccess access, bool locked = false)
	{
		return new PluginAdbCallbacks(new FakeAdbDeviceOperations(),
			new FixedAdbAccessPolicy(access),
			new FakeHostLockState { IsLocked = locked });
	}

	internal static HostInvokePayload Payload(string operation)
	{
		object arguments = operation switch
		{
			HostOperations.Adb.Shell => new AdbShellArguments { Serial = "A1", Command = "true" },
			HostOperations.Adb.Push => new AdbPushArguments
				{ Serial = "A1", LocalPath = "/tmp/a", RemotePath = "/sdcard/a" },
			HostOperations.Adb.Pull => new AdbPullArguments
				{ Serial = "A1", RemotePath = "/sdcard/a", LocalPath = "/tmp/a" },
			HostOperations.Adb.Install => new AdbInstallArguments { Serial = "A1", ApkPath = "/tmp/app.apk" },
			HostOperations.Adb.Uninstall or HostOperations.Adb.PackageInstalled
				=> new AdbPackageArguments { Serial = "A1", PackageName = "com.example.app" },
			HostOperations.Adb.Connect => new AdbConnectArguments { Address = "192.168.1.20:5555" },
			HostOperations.Adb.Key => new AdbKeyArguments { Serial = "A1", Key = AdbKeys.VolumeUp },
			HostOperations.Adb.Tap => new AdbTapArguments { Serial = "A1", X = 10, Y = 20 },
			HostOperations.Adb.Swipe => new AdbSwipeArguments
			{
				Serial = "A1", FromX = 1, FromY = 2, ToX = 3, ToY = 4, DurationMilliseconds = 250
			},
			HostOperations.Adb.InputText => new AdbTextArguments { Serial = "A1", Text = "hello" },
			HostOperations.Adb.StartApp or HostOperations.Adb.ForceStopApp
				=> new AdbPackageArguments { Serial = "A1", PackageName = "com.example.app" },
			HostOperations.Adb.OpenUri => new AdbUriArguments { Serial = "A1", Uri = "https://example.com" },
			HostOperations.Adb.Reboot => new AdbRebootArguments { Serial = "A1", Mode = AdbRebootModes.Recovery },
			_ => new AdbDeviceArguments { Serial = "A1" }
		};

		return new HostInvokePayload
		{
			Api = HostApis.Adb,
			Operation = operation,
			Arguments = JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options)
		};
	}
}

internal sealed class FakeAdbDeviceOperations : IAdbDeviceOperations
{
	public Result<AdbFailureCode> Install { get; set; } = Result.Ok<AdbFailureCode>();

	public Func<CancellationToken, Task<Result<AdbShellOutput, AdbFailureCode>>> Shell { get; set; }
		= _ => Task.FromResult(
			Result.Ok<AdbShellOutput, AdbFailureCode>(new AdbShellOutput(0, string.Empty, string.Empty, false)));

	public Task<Result<AdbShellOutput, AdbFailureCode>> RunShellAsync(
		string serial,
		string command,
		CancellationToken cancellationToken)
	{
		return Shell(cancellationToken);
	}

	public Result<AdbBatteryReading, AdbFailureCode> Battery { get; set; }
		= Result.Ok<AdbBatteryReading, AdbFailureCode>(new AdbBatteryReading(50,
			false,
			AdbBatteryStatus.Discharging,
			AdbBatteryHealth.Good));

	public Task<Result<AdbBatteryReading, AdbFailureCode>> GetBatteryAsync(
		string serial,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Battery);
	}

	public Task<Result<AdbFailureCode>> PushFileAsync(
		string serial,
		string localPath,
		string remotePath,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Result.Ok<AdbFailureCode>());
	}

	public Task<Result<AdbFailureCode>> PullFileAsync(
		string serial,
		string remotePath,
		string localPath,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Result.Ok<AdbFailureCode>());
	}

	public Task<Result<AdbFailureCode>> InstallApkAsync(
		string serial,
		string apkPath,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Install);
	}

	public Task<Result<AdbFailureCode>> UninstallPackageAsync(
		string serial,
		string packageName,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Result.Ok<AdbFailureCode>());
	}

	public Task<Result<bool, AdbFailureCode>> IsPackageInstalledAsync(
		string serial,
		string packageName,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(Result.Ok<bool, AdbFailureCode>(true));
	}

	public Func<string, Result<string, AdbFailureCode>> Connect { get; set; }
		= address => Result.Ok<string, AdbFailureCode>(address);

	public List<string> ConnectedAddresses { get; } = [];

	public Task<Result<string, AdbFailureCode>> ConnectAsync(string address, CancellationToken cancellationToken)
	{
		ConnectedAddresses.Add(address);
		return Task.FromResult(Connect(address));
	}
}

internal sealed class RecordingConsentNotifier : IPluginAdbConsentNotifier
{
	public List<string> Asked { get; } = [];

	public List<(string PluginId, bool DeclaresAdb, bool PreviouslyDeclaredAdb)> Installed { get; } = [];

	public List<string> Dismissed { get; } = [];

	public Task NotifyIfNeededAsync(
		string pluginId,
		string pluginName,
		bool declaresAdb,
		bool previouslyDeclaredAdb,
		CancellationToken cancellationToken = default)
	{
		Installed.Add((pluginId, declaresAdb, previouslyDeclaredAdb));
		return Task.CompletedTask;
	}

	public Task AskAfterRefusalAsync(string pluginId, string pluginName, CancellationToken cancellationToken = default)
	{
		Asked.Add(pluginId);
		return Task.CompletedTask;
	}

	public void Dismiss(string pluginId)
	{
		Dismissed.Add(pluginId);
	}

	public void DismissAll()
	{
	}
}
