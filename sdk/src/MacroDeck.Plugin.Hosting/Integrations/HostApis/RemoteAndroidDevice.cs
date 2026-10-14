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

	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan _rebootTimeout = TimeSpan.FromSeconds(40);
	private static readonly TimeSpan _screenshotTimeout = TimeSpan.FromSeconds(40);

	private readonly IHostInvoker _invoker;
	private readonly IPluginHostAssetReceiver? _assets;
	private readonly Lock _gate = new();
	private AndroidDeviceInfo _info = new(null, null, null);
	private AndroidDeviceState _state = AndroidDeviceState.Offline;
	private bool _lacksUsbPermission;
	private AndroidDeviceMode _mode;

	public RemoteAndroidDevice(string serial, IHostInvoker invoker, IPluginHostAssetReceiver? assets = null)
	{
		Serial = serial;
		_invoker = invoker;
		_assets = assets;
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

	public bool LacksUsbPermission
	{
		get
		{
			lock (_gate)
			{
				return _lacksUsbPermission;
			}
		}
	}

	public AndroidDeviceMode Mode
	{
		get
		{
			lock (_gate)
			{
				return _mode;
			}
		}
	}

	public async Task<AndroidShellResult> ExecuteShellAsync(
		string command,
		CancellationToken cancellationToken = default)
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

	public Task SendKeyAsync(AndroidKey key, CancellationToken cancellationToken = default)
	{
		return ToWireKey(key) is { } wireKey
			? InvokeAsync(HostOperations.Adb.Key,
				new AdbKeyArguments { Serial = Serial, Key = wireKey },
				_commandTimeout,
				cancellationToken)
			: Task.FromException(new AndroidDeviceException(AndroidDeviceErrorCode.InvalidArgument,
				$"Unknown key {key}."));
	}

	public Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
	{
		return InvokeAsync(HostOperations.Adb.Tap,
			new AdbTapArguments { Serial = Serial, X = x, Y = y },
			_commandTimeout,
			cancellationToken);
	}

	public Task SwipeAsync(
		int fromX,
		int fromY,
		int toX,
		int toY,
		TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		return InvokeAsync(HostOperations.Adb.Swipe,
			new AdbSwipeArguments
			{
				Serial = Serial,
				FromX = fromX,
				FromY = fromY,
				ToX = toX,
				ToY = toY,
				DurationMilliseconds
					= (int)Math.Clamp(Math.Round(duration.TotalMilliseconds), int.MinValue, int.MaxValue)
			},
			_commandTimeout,
			cancellationToken);
	}

	public Task InputTextAsync(string text, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(text);
		return InvokeAsync(HostOperations.Adb.InputText,
			new AdbTextArguments { Serial = Serial, Text = text },
			_commandTimeout,
			cancellationToken);
	}

	public Task StartAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return InvokeAsync(HostOperations.Adb.StartApp,
			new AdbPackageArguments { Serial = Serial, PackageName = packageName },
			_commandTimeout,
			cancellationToken);
	}

	public Task ForceStopAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(packageName);
		return InvokeAsync(HostOperations.Adb.ForceStopApp,
			new AdbPackageArguments { Serial = Serial, PackageName = packageName },
			_commandTimeout,
			cancellationToken);
	}

	public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		return InvokeAsync(HostOperations.Adb.OpenUri,
			new AdbUriArguments { Serial = Serial, Uri = uri },
			_commandTimeout,
			cancellationToken);
	}

	public Task RebootAsync(
		AndroidRebootMode mode = AndroidRebootMode.Normal,
		CancellationToken cancellationToken = default)
	{
		var wireMode = mode switch
		{
			AndroidRebootMode.Normal => AdbRebootModes.Normal,
			AndroidRebootMode.Recovery => AdbRebootModes.Recovery,
			AndroidRebootMode.Bootloader => AdbRebootModes.Bootloader,
			_ => null
		};

		return wireMode is null
			? Task.FromException(new AndroidDeviceException(AndroidDeviceErrorCode.InvalidArgument,
				$"Unknown reboot mode {mode}."))
			: InvokeAsync(HostOperations.Adb.Reboot,
				new AdbRebootArguments { Serial = Serial, Mode = wireMode },
				_rebootTimeout,
				cancellationToken);
	}

	public async Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
	{
		if (_assets is null)
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Unsupported,
				"This plugin host cannot receive a screenshot.");
		}

		var result = Read<AdbScreenshotResultDto>(await InvokeAsync(HostOperations.Adb.Screenshot,
			new AdbDeviceArguments { Serial = Serial },
			_screenshotTimeout,
			cancellationToken));

		if (string.IsNullOrEmpty(result.TransferId))
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown, "The host sent no screenshot.");
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(ProtocolTimeouts.AssetUpload);
		HostAssetTransfer transfer;
		try
		{
			transfer = await _assets.AwaitAsync(result.TransferId, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Timeout, "The screenshot did not arrive in time.");
		}

		return transfer.Accepted
			? transfer.Content
			: throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown, "The screenshot transfer failed.");
	}

	public async Task<AndroidDeviceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		JsonElement? data;
		try
		{
			data = await InvokeAsync(HostOperations.Adb.Status,
				new AdbDeviceArguments { Serial = Serial },
				ProtocolTimeouts.DefaultRequest,
				cancellationToken);
		}
		catch (AndroidDeviceException exception) when (exception.ErrorCode == AndroidDeviceErrorCode.Unsupported
			&& exception.InnerException is HostInvocationException { Code: ProtocolErrorCodes.CapabilityUnsupported })
		{
			return null;
		}

		if (data is not { ValueKind: JsonValueKind.Object })
		{
			return null;
		}

		var status = Read<AdbDeviceStatusDto>(data);
		return new AndroidDeviceStatus
		{
			IsScreenOn = status.ScreenOn,
			IsLocked = status.Locked,
			ForegroundPackage = status.ForegroundPackage
		};
	}

	internal void Update(
		AndroidDeviceInfo info,
		AndroidDeviceState state,
		bool lacksUsbPermission = false,
		AndroidDeviceMode mode = AndroidDeviceMode.Normal)
	{
		lock (_gate)
		{
			_info = info;
			_state = state;
			_lacksUsbPermission = lacksUsbPermission;
			_mode = mode;
		}
	}

	private static string? ToWireKey(AndroidKey key)
	{
		return key switch
		{
			AndroidKey.Home => AdbKeys.Home,
			AndroidKey.Back => AdbKeys.Back,
			AndroidKey.Enter => AdbKeys.Enter,
			AndroidKey.Menu => AdbKeys.Menu,
			AndroidKey.Search => AdbKeys.Search,
			AndroidKey.VolumeUp => AdbKeys.VolumeUp,
			AndroidKey.VolumeDown => AdbKeys.VolumeDown,
			AndroidKey.VolumeMute => AdbKeys.VolumeMute,
			AndroidKey.MediaPlayPause => AdbKeys.MediaPlayPause,
			AndroidKey.MediaNext => AdbKeys.MediaNext,
			AndroidKey.MediaPrevious => AdbKeys.MediaPrevious,
			AndroidKey.Power => AdbKeys.Power,
			AndroidKey.Sleep => AdbKeys.Sleep,
			AndroidKey.Wakeup => AdbKeys.Wakeup,
			AndroidKey.AppSwitch => AdbKeys.AppSwitch,
			AndroidKey.DpadUp => AdbKeys.DpadUp,
			AndroidKey.DpadDown => AdbKeys.DpadDown,
			AndroidKey.DpadLeft => AdbKeys.DpadLeft,
			AndroidKey.DpadRight => AdbKeys.DpadRight,
			AndroidKey.DpadCenter => AdbKeys.DpadCenter,
			_ => null
		};
	}

	private async Task<JsonElement?> InvokeAsync(
		string operation,
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
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown,
				"The host sent an unreadable result.",
				exception);
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
			ProtocolErrorCodes.PayloadTooLarge => AndroidDeviceErrorCode.CommandFailed,
			_ => AndroidDeviceErrorCode.Unknown
		};

		return new AndroidDeviceException(code, exception.Message, exception);
	}
}
