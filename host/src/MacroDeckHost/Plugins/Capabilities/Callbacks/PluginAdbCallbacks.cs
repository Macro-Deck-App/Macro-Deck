using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginAdbCallbacks(
	IAdbDeviceOperations operations,
	IPluginAdbAccessPolicy accessPolicy,
	IHostLockState lockState,
	IPluginAdbConsentNotifier? consent = null,
	IPluginSessionRegistry? sessions = null,
	IAdbManager? adbManager = null)
{
	// Leaves room for the envelope inside ProtocolLimits.MaxMessageBytes.
	internal const int ShellResultBudgetBytes = 192 * 1024;

	private static readonly HashSet<string> _changesSomething = new(StringComparer.Ordinal)
	{
		HostOperations.Adb.Shell,
		HostOperations.Adb.Push,
		HostOperations.Adb.Pull,
		HostOperations.Adb.Install,
		HostOperations.Adb.Uninstall,
		HostOperations.Adb.Connect
	};

	public async Task<HostCallbackResult?> AdmitAsync(string pluginId,
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

		return lockState.IsLocked && _changesSomething.Contains(payload.Operation)
			? HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"The host is locked.",
				retryable: true,
				details: Reason(ProtocolErrorReasons.HostLocked))
			: null;
	}

	public async Task<HostCallbackResult> ExecuteAsync(HostInvokePayload payload, CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.Adb.Shell when Read<AdbShellArguments>(payload) is { } arguments:
			{
				var result = await operations.RunShellAsync(arguments.Serial, arguments.Command, cancellationToken);
				return result.Success ? HostCallbackResult.Ok(FitToBudget(result.Data!)) : Failed(result.Error, result.ErrorMessage);
			}

			case HostOperations.Adb.Battery when Read<AdbDeviceArguments>(payload) is { } arguments:
			{
				var result = await operations.GetBatteryAsync(arguments.Serial, cancellationToken);
				return result.Success ? HostCallbackResult.Ok(ToDto(result.Data!)) : Failed(result.Error, result.ErrorMessage);
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
				return Completed(await operations.InstallApkAsync(arguments.Serial, arguments.ApkPath, cancellationToken));

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

	private async Task AskForAccessAsync(string pluginId, CancellationToken cancellationToken)
	{
		if (consent is null || !accessPolicy.CanBeGranted(pluginId))
		{
			return;
		}

		var name = sessions?.Snapshot().FirstOrDefault(session => session.PluginId == pluginId)?.DisplayName ?? pluginId;
		await consent.AskAfterRefusalAsync(pluginId, name, cancellationToken);
	}

	internal static AdbShellResultDto FitToBudget(AdbShellOutput output)
	{
		var dto = new AdbShellResultDto
		{
			ExitCode = output.ExitCode,
			StandardOutput = output.StandardOutput,
			StandardError = output.StandardError,
			Truncated = output.Truncated
		};

		while (JsonSerializer.SerializeToUtf8Bytes(dto, PluginProtocolJson.Options).Length > ShellResultBudgetBytes)
		{
			dto = dto.StandardOutput.Length >= dto.StandardError.Length
				? dto with { StandardOutput = dto.StandardOutput[..(dto.StandardOutput.Length / 2)], Truncated = true }
				: dto with { StandardError = dto.StandardError[..(dto.StandardError.Length / 2)], Truncated = true };
		}

		return dto;
	}

	private static AdbBatteryStateDto ToDto(AdbBatteryReading reading)
		=> new()
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

	private static HostCallbackResult Completed(Result<AdbFailureCode> result)
		=> result.Success ? HostCallbackResult.Ok() : Failed(result.Error, result.ErrorMessage);

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
		=> new(StringComparer.Ordinal) { ["reason"] = reason };

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
