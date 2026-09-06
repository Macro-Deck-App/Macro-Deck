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
	public static AdbStatus Disabled { get; } = new(Enabled: false,
		UsbConnectionsEnabled: false,
		Supported: true,
		ResolvedExecutablePath: null,
		ExecutableSource: AdbExecutableSource.None,
		AdbVersion: null,
		ServerReachable: false,
		ServerStartedByMacroDeck: false,
		DefaultDeviceSerial: null,
		LastFailure: null,
		LastFailureMessage: null,
		LastFailureAt: null,
		PreviousShutdownWasUnclean: false,
		StaleTunnelsCleaned: 0);
}
