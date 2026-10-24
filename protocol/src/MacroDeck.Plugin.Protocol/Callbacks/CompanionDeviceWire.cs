using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Converts the SDK's Companion device types to and from their <c>companion-devices</c> wire shapes, so the host and
/// the plugin side read the open strings the same way: an unknown failure reads as
/// <see cref="CompanionDeviceFailure.Failed" />, and unknown capability names pass through unchanged.
/// </summary>
public static class CompanionDeviceWire
{
	public static CompanionDeviceStateDto ToDto(CompanionDevice device)
	{
		ArgumentNullException.ThrowIfNull(device);

		var state = device.State;
		return new CompanionDeviceStateDto
		{
			DeviceId = device.Id.ToString("D"),
			ReadySequence = device.ReadySequence,
			BatteryLevelPercent = state.BatteryLevelPercent,
			Charging = state.IsCharging,
			Orientation = state.Orientation,
			ScreenBrightnessPercent = state.ScreenBrightnessPercent,
			Model = state.Model,
			Platform = state.Platform,
			AppVersion = state.AppVersion,
			FormFactor = state.FormFactor,
			Capabilities = [.. state.Capabilities.Order(StringComparer.Ordinal)],
			RequestableCapabilities = [.. state.RequestableCapabilities.Order(StringComparer.Ordinal)],
			ConfirmsCommands = state.ConfirmsCommands,
			InFocus = state.InFocus,
			NetworkType = state.NetworkType,
			NetworkMetered = state.NetworkMetered,
			NetworkValidated = state.NetworkValidated,
			NetworkName = state.NetworkName,
			CpuUsagePercent = state.CpuUsagePercent,
			MemoryUsedPercent = state.MemoryUsedPercent
		};
	}

	/// <summary>The device a pushed entry describes, or <c>null</c> when its id is not a GUID.</summary>
	public static CompanionDevice? ToDevice(CompanionDeviceStateDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		if (!Guid.TryParse(dto.DeviceId, out var id))
		{
			return null;
		}

		return new CompanionDevice(id,
			dto.ReadySequence,
			new CompanionDeviceState
			{
				BatteryLevelPercent = dto.BatteryLevelPercent,
				IsCharging = dto.Charging,
				Orientation = dto.Orientation,
				ScreenBrightnessPercent = dto.ScreenBrightnessPercent,
				Model = dto.Model,
				Platform = dto.Platform,
				AppVersion = dto.AppVersion,
				FormFactor = dto.FormFactor,
				Capabilities = (dto.Capabilities ?? []).ToHashSet(StringComparer.Ordinal),
				RequestableCapabilities = (dto.RequestableCapabilities ?? []).ToHashSet(StringComparer.Ordinal),
				ConfirmsCommands = dto.ConfirmsCommands,
				InFocus = dto.InFocus,
				NetworkType = dto.NetworkType,
				NetworkMetered = dto.NetworkMetered,
				NetworkValidated = dto.NetworkValidated,
				NetworkName = dto.NetworkName,
				CpuUsagePercent = dto.CpuUsagePercent,
				MemoryUsedPercent = dto.MemoryUsedPercent
			});
	}

	public static CompanionDeviceCommandArguments ToArguments(Guid deviceId, CompanionDeviceCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);

		return new CompanionDeviceCommandArguments
		{
			DeviceId = deviceId.ToString("D"),
			Command = command.Kind switch
			{
				CompanionDeviceCommandKind.SetBrightness => CompanionDeviceCommands.SetBrightness,
				CompanionDeviceCommandKind.SetOrientation => CompanionDeviceCommands.SetOrientation,
				CompanionDeviceCommandKind.Vibrate => CompanionDeviceCommands.Vibrate,
				CompanionDeviceCommandKind.ScreenOn => CompanionDeviceCommands.ScreenOn,
				CompanionDeviceCommandKind.ScreenOff => CompanionDeviceCommands.ScreenOff,
				CompanionDeviceCommandKind.Focus => CompanionDeviceCommands.Focus,
				_ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown command.")
			},
			BrightnessPercent = command.BrightnessPercent,
			Orientation = command.Orientation
		};
	}

	/// <summary>The command the arguments describe, or <c>null</c> when it is unknown or its values are invalid.</summary>
	public static CompanionDeviceCommand? ToCommand(CompanionDeviceCommandArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		switch (arguments.Command)
		{
			case CompanionDeviceCommands.SetBrightness when arguments.BrightnessPercent is >= 0 and <= 100:
				return CompanionDeviceCommand.SetBrightness(arguments.BrightnessPercent.Value);
			case CompanionDeviceCommands.SetOrientation when arguments.Orientation is CompanionOrientations.Automatic
				or CompanionOrientations.Portrait
				or CompanionOrientations.Landscape:
				return CompanionDeviceCommand.SetOrientation(arguments.Orientation);
			case CompanionDeviceCommands.Vibrate:
				return CompanionDeviceCommand.Vibrate;
			case CompanionDeviceCommands.ScreenOn:
				return CompanionDeviceCommand.ScreenOn;
			case CompanionDeviceCommands.ScreenOff:
				return CompanionDeviceCommand.ScreenOff;
			case CompanionDeviceCommands.Focus:
				return CompanionDeviceCommand.Focus;
			default:
				return null;
		}
	}

	public static string ToWire(CompanionScreenshotMode mode)
	{
		return mode == CompanionScreenshotMode.FullScreen
			? CompanionScreenshotModes.Full
			: CompanionScreenshotModes.Deck;
	}

	public static CompanionScreenshotMode? ToMode(string? mode)
	{
		return mode switch
		{
			CompanionScreenshotModes.Deck => CompanionScreenshotMode.Deck,
			CompanionScreenshotModes.Full => CompanionScreenshotMode.FullScreen,
			_ => null
		};
	}

	public static string ToWire(CompanionDeviceFailure failure)
	{
		return failure switch
		{
			CompanionDeviceFailure.NotConnected => CompanionDeviceFailures.NotConnected,
			CompanionDeviceFailure.Removed => CompanionDeviceFailures.Removed,
			CompanionDeviceFailure.TimedOut => CompanionDeviceFailures.TimedOut,
			CompanionDeviceFailure.ConsentDenied => CompanionDeviceFailures.ConsentDenied,
			CompanionDeviceFailure.Unavailable => CompanionDeviceFailures.Unavailable,
			_ => CompanionDeviceFailures.Failed
		};
	}

	public static CompanionDeviceFailure ToFailure(string? failure)
	{
		return failure switch
		{
			CompanionDeviceFailures.NotConnected => CompanionDeviceFailure.NotConnected,
			CompanionDeviceFailures.Removed => CompanionDeviceFailure.Removed,
			CompanionDeviceFailures.TimedOut => CompanionDeviceFailure.TimedOut,
			CompanionDeviceFailures.ConsentDenied => CompanionDeviceFailure.ConsentDenied,
			CompanionDeviceFailures.Unavailable => CompanionDeviceFailure.Unavailable,
			_ => CompanionDeviceFailure.Failed
		};
	}

	public static CompanionDeviceRequestResultDto ToDto(CompanionDeviceCommandResult result)
	{
		ArgumentNullException.ThrowIfNull(result);

		return result.Outcome switch
		{
			CompanionDeviceCommandOutcome.Done => new CompanionDeviceRequestResultDto
				{ Outcome = CompanionDeviceOutcomes.Done },
			CompanionDeviceCommandOutcome.Unconfirmed => new CompanionDeviceRequestResultDto
			{
				Outcome = CompanionDeviceOutcomes.Unconfirmed
			},
			_ => new CompanionDeviceRequestResultDto
			{
				Outcome = CompanionDeviceOutcomes.Failed,
				Failure = ToWire(result.Failure ?? CompanionDeviceFailure.Failed)
			}
		};
	}

	public static CompanionDeviceCommandResult ToResult(CompanionDeviceRequestResultDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		return dto.Outcome switch
		{
			CompanionDeviceOutcomes.Done => CompanionDeviceCommandResult.Done,
			CompanionDeviceOutcomes.Unconfirmed => CompanionDeviceCommandResult.Unconfirmed,
			_ => CompanionDeviceCommandResult.Failed(ToFailure(dto.Failure))
		};
	}
}
