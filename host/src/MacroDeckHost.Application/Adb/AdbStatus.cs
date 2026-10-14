namespace MacroDeckHost.Application.Adb;

public sealed record AdbStatus(
	bool Enabled,
	bool UsbConnectionsEnabled,
	bool Supported,
	string? ResolvedExecutablePath,
	AdbExecutableSource ExecutableSource,
	string? AdbVersion,
	bool ServerReachable,
	bool ServerStartedByMacroDeck,
	string? DefaultDeviceSerial,
	AdbFailureCode? LastFailure,
	string? LastFailureMessage,
	DateTimeOffset? LastFailureAt,
	bool PreviousShutdownWasUnclean,
	int StaleTunnelsCleaned)
{
	// Supported describes platform capability, not whether adb is enabled or installed.
	public static AdbStatus Disabled { get; } = new(false,
		false,
		true,
		null,
		AdbExecutableSource.None,
		null,
		false,
		false,
		null,
		null,
		null,
		null,
		false,
		0);
}
