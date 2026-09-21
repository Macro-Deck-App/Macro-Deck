using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAdbCallbacksTests
{
	private const string PluginId = "com.example.android";

	private static IEnumerable<TestCaseData> EveryOperation()
		=> HostOperations.Adb.All.Select(operation => new TestCaseData(operation).SetName($"{{m}}({operation})"));

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
	public async Task While_the_host_is_locked_only_operations_that_change_something_are_refused(string operation,
		bool refused)
	{
		var callbacks = Callbacks(PluginAdbAccess.Available, locked: true);

		var refusal = await callbacks.AdmitAsync(PluginId, Payload(operation), CancellationToken.None);

		Assert.That(refusal?.Error?.Details?["reason"], refused ? Is.EqualTo(ProtocolErrorReasons.HostLocked) : Is.Null);
	}

	[TestCase(AdbFailureCode.DeviceOffline, ProtocolErrorReasons.AdbDeviceOffline)]
	[TestCase(AdbFailureCode.DeviceNotFound, ProtocolErrorReasons.AdbDeviceNotFound)]
	[TestCase(AdbFailureCode.DeviceUnauthorized, ProtocolErrorReasons.AdbDeviceUnauthorized)]
	[TestCase(AdbFailureCode.ExecutableNotFound, ProtocolErrorReasons.AdbExecutableNotFound)]
	[TestCase(AdbFailureCode.Timeout, ProtocolErrorReasons.AdbTimeout)]
	[TestCase(AdbFailureCode.InvalidParameter, ProtocolErrorReasons.AdbInvalidArgument)]
	[TestCase(AdbFailureCode.CommandFailed, ProtocolErrorReasons.AdbCommandFailed)]
	public async Task A_device_or_adb_failure_is_ADB_FAILED_with_a_reason_naming_it(AdbFailureCode failure, string reason)
	{
		var operations = new FakeAdbDeviceOperations
		{
			Install = Result.Fail<AdbFailureCode>(failure, "adb said no")
		};

		var result = await new PluginAdbCallbacks(operations, new FixedAdbAccessPolicy(PluginAdbAccess.Available), new FakeHostLockState())
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
		var operations = new FakeAdbDeviceOperations { Install = Result.Fail<AdbFailureCode>(AdbFailureCode.Disabled, "off") };

		var result = await new PluginAdbCallbacks(operations, new FixedAdbAccessPolicy(PluginAdbAccess.Available), new FakeHostLockState())
			.ExecuteAsync(Payload(HostOperations.Adb.Install), CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.AdbNotEnabled));
	}

	[TestCase(PluginAdbAccess.NotEnabled, true, true)]
	[TestCase(PluginAdbAccess.NotAllowed, true, true)]
	[TestCase(PluginAdbAccess.NotAllowed, false, false)]
	[TestCase(PluginAdbAccess.Available, true, false)]
	public async Task A_refused_call_asks_the_user_only_when_allowing_adb_would_let_the_plugin_in(PluginAdbAccess access,
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

		Assert.That(result.Data?.Deserialize<AdbConnectResultDto>(PluginProtocolJson.Options)?.Serial, Is.EqualTo("192.168.1.20:5555"));
	}

	[Test]
	public async Task Missing_arguments_are_an_invalid_payload()
	{
		var result = await Callbacks(PluginAdbAccess.Available)
			.ExecuteAsync(new HostInvokePayload { Api = HostApis.Adb, Operation = HostOperations.Adb.Push }, CancellationToken.None);

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
	}

	[Test]
	public void Shell_output_that_would_not_fit_one_protocol_message_is_cut_and_marked()
	{
		var dto = PluginAdbCallbacks.FitToBudget(new AdbShellOutput(0, new string('é', 90_000), new string('', 40_000), false));

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

		Assert.That((dto.ExitCode, dto.StandardOutput, dto.StandardError, dto.Truncated), Is.EqualTo((1, "out", "err", false)));
	}

	private static PluginAdbCallbacks Callbacks(PluginAdbAccess access, bool locked = false)
		=> new(new FakeAdbDeviceOperations(), new FixedAdbAccessPolicy(access), new FakeHostLockState { IsLocked = locked });

	internal static HostInvokePayload Payload(string operation)
	{
		object arguments = operation switch
		{
			HostOperations.Adb.Shell => new AdbShellArguments { Serial = "A1", Command = "true" },
			HostOperations.Adb.Push => new AdbPushArguments { Serial = "A1", LocalPath = "/tmp/a", RemotePath = "/sdcard/a" },
			HostOperations.Adb.Pull => new AdbPullArguments { Serial = "A1", RemotePath = "/sdcard/a", LocalPath = "/tmp/a" },
			HostOperations.Adb.Install => new AdbInstallArguments { Serial = "A1", ApkPath = "/tmp/app.apk" },
			HostOperations.Adb.Uninstall or HostOperations.Adb.PackageInstalled
				=> new AdbPackageArguments { Serial = "A1", PackageName = "com.example.app" },
			HostOperations.Adb.Connect => new AdbConnectArguments { Address = "192.168.1.20:5555" },
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
		= _ => Task.FromResult(Result.Ok<AdbShellOutput, AdbFailureCode>(new AdbShellOutput(0, string.Empty, string.Empty, false)));

	public Task<Result<AdbShellOutput, AdbFailureCode>> RunShellAsync(string serial,
		string command,
		CancellationToken cancellationToken) => Shell(cancellationToken);

	public Task<Result<AdbBatteryReading, AdbFailureCode>> GetBatteryAsync(string serial, CancellationToken cancellationToken)
		=> Task.FromResult(Result.Ok<AdbBatteryReading, AdbFailureCode>(
			new AdbBatteryReading(50, false, AdbBatteryStatus.Discharging, AdbBatteryHealth.Good)));

	public Task<Result<AdbFailureCode>> PushFileAsync(string serial,
		string localPath,
		string remotePath,
		CancellationToken cancellationToken) => Task.FromResult(Result.Ok<AdbFailureCode>());

	public Task<Result<AdbFailureCode>> PullFileAsync(string serial,
		string remotePath,
		string localPath,
		CancellationToken cancellationToken) => Task.FromResult(Result.Ok<AdbFailureCode>());

	public Task<Result<AdbFailureCode>> InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken)
		=> Task.FromResult(Install);

	public Task<Result<AdbFailureCode>> UninstallPackageAsync(string serial,
		string packageName,
		CancellationToken cancellationToken) => Task.FromResult(Result.Ok<AdbFailureCode>());

	public Task<Result<bool, AdbFailureCode>> IsPackageInstalledAsync(string serial,
		string packageName,
		CancellationToken cancellationToken) => Task.FromResult(Result.Ok<bool, AdbFailureCode>(true));

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

	public Task NotifyIfNeededAsync(string pluginId,
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

	public void Dismiss(string pluginId) => Dismissed.Add(pluginId);

	public void DismissAll()
	{
	}
}
