using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Android;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteAndroidDevice : IAndroidDevice
{
	// Each is the host's own adb timeout for the operation plus room for the round trip, so the host
	// answers with its own timeout before the plugin gives up.
	private static readonly TimeSpan _shellTimeout = TimeSpan.FromSeconds(90);
	private static readonly TimeSpan _transferTimeout = TimeSpan.FromMinutes(5.5);

	private readonly IHostInvoker _invoker;
	private readonly Lock _gate = new();
	private AndroidDeviceInfo _info = new(null, null, null);
	private AndroidDeviceState _state = AndroidDeviceState.Offline;

	public RemoteAndroidDevice(string serial, IHostInvoker invoker)
	{
		Serial = serial;
		_invoker = invoker;
	}

	public string Serial { get; }

	public AndroidDeviceInfo Info
	{
		get
		{
			lock (_gate)
			{
				return _info;
			}
		}
	}

	public AndroidDeviceState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public async Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		var result = Read<AdbShellResultDto>(await InvokeAsync(HostOperations.Adb.Shell,
			new AdbShellArguments { Serial = Serial, Command = command },
			_shellTimeout,
			cancellationToken));

		return new AndroidShellResult(result.ExitCode, result.StandardOutput, result.StandardError, result.Truncated);
	}

	public async Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
	{
		var result = Read<AdbBatteryStateDto>(await InvokeAsync(HostOperations.Adb.Battery,
			new AdbDeviceArguments { Serial = Serial },
			ProtocolTimeouts.DefaultRequest,
			cancellationToken));

		return new AndroidBatteryState
		{
			Level = result.Level,
			IsCharging = result.IsCharging,
			Status = result.Status switch
			{
				AdbBatteryStatuses.Charging => AndroidBatteryStatus.Charging,
				AdbBatteryStatuses.Discharging => AndroidBatteryStatus.Discharging,
				AdbBatteryStatuses.NotCharging => AndroidBatteryStatus.NotCharging,
				AdbBatteryStatuses.Full => AndroidBatteryStatus.Full,
				_ => AndroidBatteryStatus.Unknown
			},
			Health = result.Health switch
			{
				AdbBatteryHealths.Good => AndroidBatteryHealth.Good,
				AdbBatteryHealths.Overheat => AndroidBatteryHealth.Overheat,
				AdbBatteryHealths.Dead => AndroidBatteryHealth.Dead,
				AdbBatteryHealths.OverVoltage => AndroidBatteryHealth.OverVoltage,
				AdbBatteryHealths.Failure => AndroidBatteryHealth.Failure,
				AdbBatteryHealths.Cold => AndroidBatteryHealth.Cold,
				_ => AndroidBatteryHealth.Unknown
			}
		};
	}

	public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(localPath);
		ArgumentNullException.ThrowIfNull(remotePath);
		return InvokeAsync(HostOperations.Adb.Push,
			new AdbPushArguments { Serial = Serial, LocalPath = localPath, RemotePath = remotePath },
			_transferTimeout,
			cancellationToken);
	}

	public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(remotePath);
		ArgumentNullException.ThrowIfNull(localPath);
		return InvokeAsync(HostOperations.Adb.Pull,
			new AdbPullArguments { Serial = Serial, RemotePath = remotePath, LocalPath = localPath },
			_transferTimeout,
			cancellationToken);
	}

	public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(apkPath);
		return InvokeAsync(HostOperations.Adb.Install,
			new AdbInstallArguments { Serial = Serial, ApkPath = apkPath },
			_transferTimeout,
			cancellationToken);
	}

	public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return InvokeAsync(HostOperations.Adb.Uninstall,
			new AdbPackageArguments { Serial = Serial, PackageName = packageName },
			_shellTimeout,
			cancellationToken);
	}

	public async Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return Read<AdbPackageInstalledDto>(await InvokeAsync(HostOperations.Adb.PackageInstalled,
				new AdbPackageArguments { Serial = Serial, PackageName = packageName },
				ProtocolTimeouts.DefaultRequest,
				cancellationToken))
			.Installed;
	}

	internal void Update(AndroidDeviceInfo info, AndroidDeviceState state)
	{
		lock (_gate)
		{
			_info = info;
			_state = state;
		}
	}

	private async Task<JsonElement?> InvokeAsync(string operation,
		object arguments,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Adb,
					operation,
					arguments,
					timeout,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw ToAndroidDeviceException(exception);
		}
	}

	private static T Read<T>(JsonElement? data)
		where T : class
	{
		try
		{
			if (data?.Deserialize<T>(PluginProtocolJson.Options) is { } result)
			{
				return result;
			}
		}
		catch (JsonException exception)
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown, "The host sent an unreadable result.", exception);
		}

		throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown, "The host sent no result.");
	}

	internal static AndroidDeviceException ToAndroidDeviceException(HostInvocationException exception)
	{
		var reason = exception.Details.GetValueOrDefault("reason");
		var code = exception.Code switch
		{
			ProtocolErrorCodes.AdbNotEnabled => AndroidDeviceErrorCode.AdbNotEnabled,
			ProtocolErrorCodes.AdbNotAllowed => AndroidDeviceErrorCode.AdbNotAllowed,
			ProtocolErrorCodes.AdbFailed => reason switch
			{
				ProtocolErrorReasons.AdbExecutableNotFound or ProtocolErrorReasons.AdbServerUnreachable
					=> AndroidDeviceErrorCode.AdbUnavailable,
				ProtocolErrorReasons.AdbDeviceNotFound => AndroidDeviceErrorCode.DeviceNotFound,
				ProtocolErrorReasons.AdbDeviceOffline => AndroidDeviceErrorCode.DeviceOffline,
				ProtocolErrorReasons.AdbDeviceUnauthorized => AndroidDeviceErrorCode.DeviceUnauthorized,
				ProtocolErrorReasons.AdbTimeout => AndroidDeviceErrorCode.Timeout,
				ProtocolErrorReasons.AdbCommandFailed => AndroidDeviceErrorCode.CommandFailed,
				ProtocolErrorReasons.AdbInvalidArgument => AndroidDeviceErrorCode.InvalidArgument,
				ProtocolErrorReasons.AdbUnsupported => AndroidDeviceErrorCode.Unsupported,
				_ => AndroidDeviceErrorCode.Unknown
			},
			ProtocolErrorCodes.CapabilityUnavailable when reason == ProtocolErrorReasons.HostLocked
				=> AndroidDeviceErrorCode.HostLocked,
			ProtocolErrorCodes.CapabilityUnavailable => AndroidDeviceErrorCode.HostUnavailable,
			ProtocolErrorCodes.CapabilityUnsupported => AndroidDeviceErrorCode.Unsupported,
			ProtocolErrorCodes.Timeout => AndroidDeviceErrorCode.Timeout,
			ProtocolErrorCodes.RateLimited => AndroidDeviceErrorCode.RateLimited,
			ProtocolErrorCodes.InvalidPayload => AndroidDeviceErrorCode.InvalidArgument,
			_ => AndroidDeviceErrorCode.Unknown
		};

		return new AndroidDeviceException(code, exception.Message, exception);
	}
}
