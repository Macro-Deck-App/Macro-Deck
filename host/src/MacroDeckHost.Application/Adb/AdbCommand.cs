namespace MacroDeckHost.Application.Adb;

public abstract record AdbCommand(string Serial);

/// <summary>
/// A command that only reads. Separated from the rest so the manager can offer device output for
/// exactly these and nothing else - an action's stdout is never a contract.
/// </summary>
public abstract record AdbQueryCommand(string Serial) : AdbCommand(Serial);

public sealed record AdbKeyEventCommand(string Serial, AdbKey Key) : AdbCommand(Serial);

public sealed record AdbStartAppCommand(string Serial, string Package) : AdbCommand(Serial);

public sealed record AdbForceStopAppCommand(string Serial, string Package) : AdbCommand(Serial);

public sealed record AdbOpenUriCommand(string Serial, string Uri) : AdbCommand(Serial);

public sealed record AdbInputTextCommand(string Serial, string Text) : AdbCommand(Serial);

public sealed record AdbTapCommand(string Serial, int X, int Y) : AdbCommand(Serial);

public sealed record AdbSwipeCommand(
	string Serial,
	int X1,
	int Y1,
	int X2,
	int Y2,
	int DurationMs) : AdbCommand(Serial);

public sealed record AdbRebootCommand(string Serial, AdbRebootMode Mode) : AdbCommand(Serial);

public sealed record AdbScreenshotCommand(string Serial) : AdbCommand(Serial);

/// <summary>
/// Copies a local file onto the device (issue #727). Device provisioning needs to place a small
/// configuration file; it deliberately does not gain a way to run an arbitrary device-side command,
/// which is what keeps <see cref="IAdbManager"/>'s command set closed (ADR 0030).
/// </summary>
public sealed record AdbPushFileCommand(string Serial, string LocalPath, string DevicePath) : AdbCommand(Serial);

/// <summary>
/// Restarts one service on the device. The mechanism is a closed set rather than a command string:
/// firmware variants differ in which init system they run, not in what may be executed.
/// </summary>
/// <summary>
/// Remounts the device's root filesystem read-write. Appliance firmwares ship it read-only, so a
/// configuration write fails without this. Fixed command with no parameters beyond the serial.
/// </summary>
public sealed record AdbRemountRootWritableCommand(string Serial) : AdbCommand(Serial);

/// <summary>
/// Lists the services the device's init system knows about. Read-only, and the whole command is
/// fixed - the only variable is which of the closed set of init systems to ask.
/// </summary>
public sealed record AdbListServicesCommand(string Serial, AdbServiceManager Manager) : AdbQueryCommand(Serial);

/// <summary>
/// Tests whether a directory exists on the device. Read-only; the path is validated the same way a
/// write target is, so this cannot be used to smuggle a second command onto the device.
/// </summary>
public sealed record AdbDirectoryExistsCommand(string Serial, string DevicePath) : AdbQueryCommand(Serial);

public sealed record AdbRestartServiceCommand(
	string Serial,
	AdbServiceManager Manager,
	string ServiceName) : AdbCommand(Serial);
