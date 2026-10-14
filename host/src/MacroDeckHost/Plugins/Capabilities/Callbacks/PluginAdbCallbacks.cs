using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginAdbCallbacks(
	IAdbDeviceOperations operations,
	IPluginAdbAccessPolicy accessPolicy,
	IHostLockState lockState,
	IPluginAdbConsentNotifier? consent = null,
	IPluginSessionRegistry? sessions = null,
	IAdbManager? adbManager = null,
	IPluginHostAssetSender? assets = null)
{
	internal const int ShellResultBudgetBytes = AdbDeviceCallPolicy.ShellResultBudgetBytes;

	private readonly AdbDeviceCallPolicy _callPolicy = new(lockState);

	public async Task<HostCallbackResult?> AdmitAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (await accessPolicy.EvaluateAsync(pluginId, cancellationToken))
		{
			case PluginAdbAccess.NotEnabled:
				await AskForAccessAsync(pluginId, cancellationToken);
				return HostCallbackResult.Fail(ProtocolErrorCodes.AdbNotEnabled,
					ProtocolErrorMessages.For(ProtocolErrorCodes.AdbNotEnabled));
			case PluginAdbAccess.NotAllowed:
				await AskForAccessAsync(pluginId, cancellationToken);
				return HostCallbackResult.Fail(ProtocolErrorCodes.AdbNotAllowed,
					ProtocolErrorMessages.For(ProtocolErrorCodes.AdbNotAllowed));
		}

		if (adbManager?.Status is { Supported: true, ResolvedExecutablePath: null })
		{
			await AskForAccessAsync(pluginId, cancellationToken);
		}

		return _callPolicy.IsRefusedWhileLocked(payload.Operation)
			? HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"The host is locked.",
				true,
				Reason(ProtocolErrorReasons.HostLocked))
			: null;
	}

	public Task<HostCallbackResult> ExecuteAsync(HostInvokePayload payload, CancellationToken cancellationToken)
	{
		return ExecuteAsync(string.Empty, payload, cancellationToken);
	}

	public async Task<HostCallbackResult> ExecuteAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (IsTyped(payload.Operation))
		{
			return await ExecuteTypedAsync(pluginId, payload, cancellationToken);
		}

		switch (payload.Operation)
		{
			case HostOperations.Adb.Shell when Read<AdbShellArguments>(payload) is { } arguments:
			{
				var result = await operations.RunShellAsync(arguments.Serial, arguments.Command, cancellationToken);
				return result.Success
					? HostCallbackResult.Ok(FitToBudget(result.Data!))
					: Failed(result.Error, result.ErrorMessage);
			}

			case HostOperations.Adb.Battery when Read<AdbDeviceArguments>(payload) is { } arguments:
			{
				var result = await operations.GetBatteryAsync(arguments.Serial, cancellationToken);
				return result.Success
					? HostCallbackResult.Ok(ToDto(result.Data!))
					: Failed(result.Error, result.ErrorMessage);
			}

			case HostOperations.Adb.Push when Read<AdbPushArguments>(payload) is { } arguments:
				return Completed(await operations.PushFileAsync(arguments.Serial,
					arguments.LocalPath,
					arguments.RemotePath,
					cancellationToken));

			case HostOperations.Adb.Pull when Read<AdbPullArguments>(payload) is { } arguments:
				return Completed(await operations.PullFileAsync(arguments.Serial,
					arguments.RemotePath,
					arguments.LocalPath,
					cancellationToken));

			case HostOperations.Adb.Install when Read<AdbInstallArguments>(payload) is { } arguments:
				return Completed(
					await operations.InstallApkAsync(arguments.Serial, arguments.ApkPath, cancellationToken));

			case HostOperations.Adb.Uninstall when Read<AdbPackageArguments>(payload) is { } arguments:
				return Completed(await operations.UninstallPackageAsync(arguments.Serial,
					arguments.PackageName,
					cancellationToken));

			case HostOperations.Adb.PackageInstalled when Read<AdbPackageArguments>(payload) is { } arguments:
			{
				var result = await operations.IsPackageInstalledAsync(arguments.Serial,
					arguments.PackageName,
					cancellationToken);
				return result.Success
					? HostCallbackResult.Ok(new AdbPackageInstalledDto { Installed = result.Data })
					: Failed(result.Error, result.ErrorMessage);
			}

			case HostOperations.Adb.Connect when Read<AdbConnectArguments>(payload) is { } arguments:
			{
				var result = await operations.ConnectAsync(arguments.Address, cancellationToken);
				return result.Success
					? HostCallbackResult.Ok(new AdbConnectResultDto { Serial = result.Data! })
					: Failed(result.Error, result.ErrorMessage);
			}

			default:
				return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
					ProtocolErrorMessages.For(ProtocolErrorCodes.InvalidPayload));
		}
	}

	private static bool IsTyped(string operation)
	{
		return operation is HostOperations.Adb.Key
			or HostOperations.Adb.Tap
			or HostOperations.Adb.Swipe
			or HostOperations.Adb.InputText
			or HostOperations.Adb.StartApp
			or HostOperations.Adb.ForceStopApp
			or HostOperations.Adb.OpenUri
			or HostOperations.Adb.Reboot
			or HostOperations.Adb.Screenshot
			or HostOperations.Adb.Status;
	}

	private async Task<HostCallbackResult> ExecuteTypedAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (adbManager is null)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
				ProtocolErrorMessages.For(ProtocolErrorCodes.CapabilityUnsupported));
		}

		// An empty serial would make the adb manager pick the default device, which a plugin must name itself.
		if (Read<AdbDeviceArguments>(payload) is not { Serial: { } serial } || string.IsNullOrWhiteSpace(serial))
		{
			return Failed(AdbFailureCode.InvalidParameter, "A device serial is required.");
		}

		switch (payload.Operation)
		{
			case HostOperations.Adb.Key when Read<AdbKeyArguments>(payload) is { } arguments:
				return ToAdbKey(arguments.Key) is { } key
					? await RunAsync(new AdbKeyEventCommand(serial, key), cancellationToken)
					: Failed(AdbFailureCode.InvalidParameter, "Unknown key.");

			case HostOperations.Adb.Tap when Read<AdbTapArguments>(payload) is { } arguments:
				return await RunAsync(new AdbTapCommand(serial, arguments.X, arguments.Y), cancellationToken);

			case HostOperations.Adb.Swipe when Read<AdbSwipeArguments>(payload) is { } arguments:
				return await RunAsync(new AdbSwipeCommand(serial,
						arguments.FromX,
						arguments.FromY,
						arguments.ToX,
						arguments.ToY,
						arguments.DurationMilliseconds),
					cancellationToken);

			case HostOperations.Adb.InputText when Read<AdbTextArguments>(payload) is { } arguments:
				return await RunAsync(new AdbInputTextCommand(serial, arguments.Text), cancellationToken);

			case HostOperations.Adb.StartApp when Read<AdbPackageArguments>(payload) is { } arguments:
				return await RunAsync(new AdbStartAppCommand(serial, arguments.PackageName), cancellationToken);

			case HostOperations.Adb.ForceStopApp when Read<AdbPackageArguments>(payload) is { } arguments:
				return await RunAsync(new AdbForceStopAppCommand(serial, arguments.PackageName), cancellationToken);

			case HostOperations.Adb.OpenUri when Read<AdbUriArguments>(payload) is { } arguments:
				return await RunAsync(new AdbOpenUriCommand(serial, arguments.Uri), cancellationToken);

			case HostOperations.Adb.Reboot when Read<AdbRebootArguments>(payload) is { } arguments:
				return ToAdbRebootMode(arguments.Mode) is { } mode
					? await RunAsync(new AdbRebootCommand(serial, mode), cancellationToken)
					: Failed(AdbFailureCode.InvalidParameter, "Unknown reboot mode.");

			case HostOperations.Adb.Screenshot:
				return await ScreenshotAsync(pluginId, serial, cancellationToken);

			case HostOperations.Adb.Status:
			{
				var properties = await adbManager.GetPropertiesAsync(serial, cancellationToken);
				return properties is null
					? HostCallbackResult.Ok()
					: HostCallbackResult.Ok(new AdbDeviceStatusDto
					{
						ScreenOn = properties.ScreenOn,
						Locked = properties.Locked,
						ForegroundPackage = properties.ForegroundPackage
					});
			}

			default:
				return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
					ProtocolErrorMessages.For(ProtocolErrorCodes.InvalidPayload));
		}
	}

	private async Task<HostCallbackResult> RunAsync(AdbCommand command, CancellationToken cancellationToken)
	{
		return Completed(await adbManager!.ExecuteAsync(command, cancellationToken));
	}

	// The bytes cannot ride the reply, which is capped at the protocol message size, so they follow over
	// host.asset like a devices icon. The transfer is not awaited: the plugin correlates it by its id.
	private async Task<HostCallbackResult> ScreenshotAsync(
		string pluginId,
		string serial,
		CancellationToken cancellationToken)
	{
		if (assets is null || string.IsNullOrEmpty(pluginId))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"This host cannot transfer a screenshot to a plugin.");
		}

		var result = await adbManager!.CaptureScreenshotAsync(serial, cancellationToken);
		if (!result.Success)
		{
			return Failed(result.Error, result.ErrorMessage);
		}

		var png = result.Data!;
		if (png.Length > ProtocolLimits.MaxAssetBytes)
		{
			return Failed(AdbFailureCode.CommandFailed, "The screenshot is larger than a plugin can receive.");
		}

		var transferId = Guid.CreateVersion7().ToString();
		var contentHash = AssetContentHash.Compute(png);
		var sender = assets;
		_ = Task.Run(() => sender.SendAsync(pluginId,
				transferId,
				AssetKinds.AndroidScreenshot,
				"image/png",
				png,
				contentHash,
				CancellationToken.None),
			CancellationToken.None);

		return HostCallbackResult.Ok(new AdbScreenshotResultDto
		{
			ContentType = "image/png",
			ByteLength = png.Length,
			ContentHash = contentHash,
			TransferId = transferId
		});
	}

	private static AdbKey? ToAdbKey(string? key)
	{
		return key switch
		{
			AdbKeys.Home => AdbKey.Home,
			AdbKeys.Back => AdbKey.Back,
			AdbKeys.Enter => AdbKey.Enter,
			AdbKeys.Menu => AdbKey.Menu,
			AdbKeys.Search => AdbKey.Search,
			AdbKeys.VolumeUp => AdbKey.VolumeUp,
			AdbKeys.VolumeDown => AdbKey.VolumeDown,
			AdbKeys.VolumeMute => AdbKey.VolumeMute,
			AdbKeys.MediaPlayPause => AdbKey.MediaPlayPause,
			AdbKeys.MediaNext => AdbKey.MediaNext,
			AdbKeys.MediaPrevious => AdbKey.MediaPrevious,
			AdbKeys.Power => AdbKey.Power,
			AdbKeys.Sleep => AdbKey.Sleep,
			AdbKeys.Wakeup => AdbKey.Wakeup,
			AdbKeys.AppSwitch => AdbKey.AppSwitch,
			AdbKeys.DpadUp => AdbKey.DpadUp,
			AdbKeys.DpadDown => AdbKey.DpadDown,
			AdbKeys.DpadLeft => AdbKey.DpadLeft,
			AdbKeys.DpadRight => AdbKey.DpadRight,
			AdbKeys.DpadCenter => AdbKey.DpadCenter,
			_ => null
		};
	}

	private static AdbRebootMode? ToAdbRebootMode(string? mode)
	{
		return mode switch
		{
			AdbRebootModes.Normal => AdbRebootMode.Normal,
			AdbRebootModes.Recovery => AdbRebootMode.Recovery,
			AdbRebootModes.Bootloader => AdbRebootMode.Bootloader,
			_ => null
		};
	}

	private async Task AskForAccessAsync(string pluginId, CancellationToken cancellationToken)
	{
		if (consent is null || !accessPolicy.CanBeGranted(pluginId))
		{
			return;
		}

		var name = sessions?.Snapshot().FirstOrDefault(session => session.PluginId == pluginId)?.DisplayName
			?? pluginId;
		await consent.AskAfterRefusalAsync(pluginId, name, cancellationToken);
	}

	internal static AdbShellResultDto FitToBudget(AdbShellOutput output)
	{
		return AdbDeviceCallPolicy.FitToBudget(output);
	}

	private static AdbBatteryStateDto ToDto(AdbBatteryReading reading)
	{
		return new AdbBatteryStateDto
		{
			Level = reading.Level,
			IsCharging = reading.IsPluggedIn,
			Status = reading.Status switch
			{
				AdbBatteryStatus.Charging => AdbBatteryStatuses.Charging,
				AdbBatteryStatus.Discharging => AdbBatteryStatuses.Discharging,
				AdbBatteryStatus.NotCharging => AdbBatteryStatuses.NotCharging,
				AdbBatteryStatus.Full => AdbBatteryStatuses.Full,
				_ => AdbBatteryStatuses.Unknown
			},
			Health = reading.Health switch
			{
				AdbBatteryHealth.Good => AdbBatteryHealths.Good,
				AdbBatteryHealth.Overheat => AdbBatteryHealths.Overheat,
				AdbBatteryHealth.Dead => AdbBatteryHealths.Dead,
				AdbBatteryHealth.OverVoltage => AdbBatteryHealths.OverVoltage,
				AdbBatteryHealth.Failure => AdbBatteryHealths.Failure,
				AdbBatteryHealth.Cold => AdbBatteryHealths.Cold,
				_ => AdbBatteryHealths.Unknown
			}
		};
	}

	private static HostCallbackResult Completed(Result<AdbFailureCode> result)
	{
		return result.Success ? HostCallbackResult.Ok() : Failed(result.Error, result.ErrorMessage);
	}

	private static HostCallbackResult Failed(AdbFailureCode? code, string? message)
	{
		if (code == AdbFailureCode.Disabled)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.AdbNotEnabled,
				ProtocolErrorMessages.For(ProtocolErrorCodes.AdbNotEnabled));
		}

		var reason = code switch
		{
			AdbFailureCode.ExecutableNotFound => ProtocolErrorReasons.AdbExecutableNotFound,
			AdbFailureCode.ServerUnreachable => ProtocolErrorReasons.AdbServerUnreachable,
			AdbFailureCode.DeviceNotFound => ProtocolErrorReasons.AdbDeviceNotFound,
			AdbFailureCode.DeviceUnauthorized => ProtocolErrorReasons.AdbDeviceUnauthorized,
			AdbFailureCode.DeviceOffline => ProtocolErrorReasons.AdbDeviceOffline,
			AdbFailureCode.Timeout => ProtocolErrorReasons.AdbTimeout,
			AdbFailureCode.InvalidParameter => ProtocolErrorReasons.AdbInvalidArgument,
			AdbFailureCode.Unsupported => ProtocolErrorReasons.AdbUnsupported,
			_ => ProtocolErrorReasons.AdbCommandFailed
		};

		return HostCallbackResult.Fail(ProtocolErrorCodes.AdbFailed,
			string.IsNullOrWhiteSpace(message) ? ProtocolErrorMessages.For(ProtocolErrorCodes.AdbFailed) : message,
			details: Reason(reason));
	}

	private static Dictionary<string, string> Reason(string reason)
	{
		return new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason };
	}

	private static T? Read<T>(HostInvokePayload payload)
		where T : class
	{
		try
		{
			return payload.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
