using System.Text.RegularExpressions;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Infrastructure.Adb;

internal static partial class AdbCommandBuilder
{
	private const int MaxCoordinate = 8192;
	private const int MaxSwipeDurationMs = 5000;
	private const int MaxPackageLength = 255;
	private const int MaxUriLength = 2000;
	private const int MaxTextLength = 500;
	private const int MaxDevicePathLength = 512;
	private const int MaxServiceNameLength = 128;

	public static Result<IReadOnlyList<string>, AdbFailureCode> Build(AdbCommand command)
	{
		var serialFailure = ValidateSerial(command.Serial);
		if (serialFailure is not null)
		{
			return serialFailure;
		}

		return command switch
		{
			AdbKeyEventCommand keyEvent => BuildKeyEvent(keyEvent),
			AdbStartAppCommand startApp => BuildStartApp(startApp),
			AdbForceStopAppCommand forceStop => BuildForceStopApp(forceStop),
			AdbOpenUriCommand openUri => BuildOpenUri(openUri),
			AdbInputTextCommand inputText => BuildInputText(inputText),
			AdbTapCommand tap => BuildTap(tap),
			AdbSwipeCommand swipe => BuildSwipe(swipe),
			AdbRebootCommand reboot => BuildReboot(reboot),
			AdbScreenshotCommand screenshot => BuildScreenshot(screenshot),
			AdbPushFileCommand push => BuildPushFile(push),
			AdbRestartServiceCommand restartService => BuildRestartService(restartService),
			AdbListServicesCommand listServices => BuildListServices(listServices),
			AdbDirectoryExistsCommand directoryExists => BuildDirectoryExists(directoryExists),
			AdbRemountRootWritableCommand remount => BuildRemountRootWritable(remount),
			_ => Fail("Unknown command type.")
		};
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildKeyEvent(AdbKeyEventCommand command)
		=> KeyCodeFor(command.Key) is { } keyCode
			? Shell(command.Serial, $"input keyevent {keyCode}")
			: Fail("Unknown key.");

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildStartApp(AdbStartAppCommand command)
	{
		var packageFailure = ValidatePackage(command.Package);
		if (packageFailure is not null)
		{
			return packageFailure;
		}

		return Shell(command.Serial,
			$"monkey -p {AdbShellQuote.Quote(command.Package)} -c android.intent.category.LAUNCHER 1");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildForceStopApp(AdbForceStopAppCommand command)
	{
		var packageFailure = ValidatePackage(command.Package);
		if (packageFailure is not null)
		{
			return packageFailure;
		}

		return Shell(command.Serial, $"am force-stop {AdbShellQuote.Quote(command.Package)}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildOpenUri(AdbOpenUriCommand command)
	{
		if (command.Uri.Length > MaxUriLength)
		{
			return Fail("Uri is too long.");
		}

		if (!Uri.TryCreate(command.Uri, UriKind.Absolute, out var parsed) ||
			parsed.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) ||
			parsed.Scheme.Equals("content", StringComparison.OrdinalIgnoreCase))
		{
			return Fail("Uri must be absolute and must not use the file or content scheme.");
		}

		return Shell(command.Serial, $"am start -a android.intent.action.VIEW -d {AdbShellQuote.Quote(command.Uri)}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildInputText(AdbInputTextCommand command)
	{
		if (command.Text.Length > MaxTextLength)
		{
			return Fail("Text is too long.");
		}

		if (!IsPrintableAscii(command.Text))
		{
			return Fail("Text must contain only printable ASCII characters.");
		}

		// Single-quoting already carries spaces through literally; also substituting "%s" for spaces
		// (as some adb docs suggest) would double-escape and corrupt the text.
		return Shell(command.Serial, $"input text {AdbShellQuote.Quote(command.Text)}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildTap(AdbTapCommand command)
	{
		if (!IsValidCoordinate(command.X) || !IsValidCoordinate(command.Y))
		{
			return Fail("Tap coordinates must be within 0..8192.");
		}

		return Shell(command.Serial, $"input tap {command.X} {command.Y}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildSwipe(AdbSwipeCommand command)
	{
		if (!IsValidCoordinate(command.X1) ||
			!IsValidCoordinate(command.Y1) ||
			!IsValidCoordinate(command.X2) ||
			!IsValidCoordinate(command.Y2))
		{
			return Fail("Swipe coordinates must be within 0..8192.");
		}

		if (command.DurationMs is < 0 or > MaxSwipeDurationMs)
		{
			return Fail("Swipe duration must be within 0..5000 ms.");
		}

		return Shell(command.Serial,
			$"input swipe {command.X1} {command.Y1} {command.X2} {command.Y2} {command.DurationMs}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildReboot(AdbRebootCommand command)
	{
		IReadOnlyList<string> argv = command.Mode switch
		{
			AdbRebootMode.Recovery => ["-s", command.Serial, "reboot", "recovery"],
			AdbRebootMode.Bootloader => ["-s", command.Serial, "reboot", "bootloader"],
			_ => ["-s", command.Serial, "reboot"]
		};

		return Result.Ok<IReadOnlyList<string>, AdbFailureCode>(argv);
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildScreenshot(AdbScreenshotCommand command)
		=> Result.Ok<IReadOnlyList<string>, AdbFailureCode>(["-s", command.Serial, "exec-out", "screencap -p"]);

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildPushFile(AdbPushFileCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.LocalPath))
		{
			return Fail("Local path is required.");
		}

		var pathFailure = ValidateDevicePath(command.DevicePath);
		if (pathFailure is not null)
		{
			return pathFailure;
		}

		// push takes both paths as argv entries, so ArgumentList quoting is the whole story here -
		// unlike `adb shell`, nothing is handed to a device-side shell to re-parse.
		return Result.Ok<IReadOnlyList<string>, AdbFailureCode>([
			"-s", command.Serial, "push", command.LocalPath, command.DevicePath
		]);
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildListServices(AdbListServicesCommand command)
		=> command.Manager switch
		{
			AdbServiceManager.Supervisord => Shell(command.Serial, "supervisorctl status"),
			AdbServiceManager.Systemd => Shell(command.Serial, "systemctl list-units --type=service --no-pager"),
			AdbServiceManager.SysVInit => Shell(command.Serial, "ls -1 /etc/init.d"),
			_ => Fail("Unknown service manager.")
		};

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildDirectoryExists(
		AdbDirectoryExistsCommand command)
	{
		var pathFailure = ValidateDevicePath(command.DevicePath);
		if (pathFailure is not null)
		{
			return pathFailure;
		}

		return Shell(command.Serial, $"test -d {AdbShellQuote.Quote(command.DevicePath)}");
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildRestartService(AdbRestartServiceCommand command)
	{
		var serviceFailure = ValidateServiceName(command.ServiceName);
		if (serviceFailure is not null)
		{
			return serviceFailure;
		}

		var quoted = AdbShellQuote.Quote(command.ServiceName);
		return command.Manager switch
		{
			AdbServiceManager.Supervisord => Shell(command.Serial, $"supervisorctl restart {quoted}"),
			AdbServiceManager.Systemd => Shell(command.Serial, $"systemctl restart {quoted}"),
			AdbServiceManager.SysVInit => Shell(command.Serial, $"/etc/init.d/{quoted} restart"),
			_ => Fail("Unknown service manager.")
		};
	}

	private static Result<IReadOnlyList<string>, AdbFailureCode> BuildRemountRootWritable(
		AdbRemountRootWritableCommand command)
		=> Shell(command.Serial, "mount -o remount,rw /");

	private static Result<IReadOnlyList<string>, AdbFailureCode> Shell(string serial, string shellCommand)
		=> Result.Ok<IReadOnlyList<string>, AdbFailureCode>(["-s", serial, "shell", shellCommand]);

	private static Result<IReadOnlyList<string>, AdbFailureCode>? ValidateSerial(string serial)
		=> !string.IsNullOrWhiteSpace(serial) && SerialRegex().IsMatch(serial) ? null : Fail("Invalid serial.");

	/// <summary>
	/// Absolute, traversal-free and drawn from a closed character set. A device path becomes a write
	/// target, so "looks reasonable" is not enough - anything that could climb out of the directory
	/// the caller meant is refused rather than normalised.
	/// </summary>
	private static Result<IReadOnlyList<string>, AdbFailureCode>? ValidateDevicePath(string devicePath)
		=> devicePath.Length <= MaxDevicePathLength &&
			devicePath.StartsWith('/') &&
			!devicePath.Contains("..", StringComparison.Ordinal) &&
			DevicePathRegex().IsMatch(devicePath)
				? null
				: Fail("Invalid device path.");

	private static Result<IReadOnlyList<string>, AdbFailureCode>? ValidateServiceName(string serviceName)
		=> serviceName.Length <= MaxServiceNameLength && ServiceNameRegex().IsMatch(serviceName)
			? null
			: Fail("Invalid service name.");

	private static Result<IReadOnlyList<string>, AdbFailureCode>? ValidatePackage(string package)
		=> package.Length <= MaxPackageLength && PackageRegex().IsMatch(package) ? null : Fail("Invalid package name.");

	private static Result<IReadOnlyList<string>, AdbFailureCode> Fail(string message)
		=> Result.Fail<IReadOnlyList<string>, AdbFailureCode>(AdbFailureCode.InvalidParameter, message);

	private static bool IsValidCoordinate(int value) => value is >= 0 and <= MaxCoordinate;

	private static bool IsPrintableAscii(string value)
	{
		foreach (var character in value)
		{
			if (character is < (char)0x20 or > (char)0x7E)
			{
				return false;
			}
		}

		return true;
	}

	private static string? KeyCodeFor(AdbKey key)
		=> key switch
		{
			AdbKey.Home => "KEYCODE_HOME",
			AdbKey.Back => "KEYCODE_BACK",
			AdbKey.Enter => "KEYCODE_ENTER",
			AdbKey.Menu => "KEYCODE_MENU",
			AdbKey.Search => "KEYCODE_SEARCH",
			AdbKey.VolumeUp => "KEYCODE_VOLUME_UP",
			AdbKey.VolumeDown => "KEYCODE_VOLUME_DOWN",
			AdbKey.VolumeMute => "KEYCODE_VOLUME_MUTE",
			AdbKey.MediaPlayPause => "KEYCODE_MEDIA_PLAY_PAUSE",
			AdbKey.MediaNext => "KEYCODE_MEDIA_NEXT",
			AdbKey.MediaPrevious => "KEYCODE_MEDIA_PREVIOUS",
			AdbKey.Power => "KEYCODE_POWER",
			AdbKey.Sleep => "KEYCODE_SLEEP",
			AdbKey.Wakeup => "KEYCODE_WAKEUP",
			AdbKey.AppSwitch => "KEYCODE_APP_SWITCH",
			AdbKey.DpadUp => "KEYCODE_DPAD_UP",
			AdbKey.DpadDown => "KEYCODE_DPAD_DOWN",
			AdbKey.DpadLeft => "KEYCODE_DPAD_LEFT",
			AdbKey.DpadRight => "KEYCODE_DPAD_RIGHT",
			AdbKey.DpadCenter => "KEYCODE_DPAD_CENTER",
			_ => null
		};

	[GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,128}$")]
	private static partial Regex SerialRegex();

	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$")]
	private static partial Regex PackageRegex();

	[GeneratedRegex(@"^/[A-Za-z0-9._/\-]+$")]
	private static partial Regex DevicePathRegex();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@\-]*$")]
	private static partial Regex ServiceNameRegex();
}
