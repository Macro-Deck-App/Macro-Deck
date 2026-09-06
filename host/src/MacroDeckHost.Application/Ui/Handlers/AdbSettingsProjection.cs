using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

internal readonly record struct AdbSettingsProjection(
	bool Enabled,
	string? ExecutablePath,
	string? ResolvedExecutablePath,
	string ExecutableSource,
	string? AdbVersion,
	bool ServerReachable,
	bool ServerStartedByMacroDeck,
	bool UsbConnectionsEnabled,
	string? DefaultDeviceSerial,
	int ActivePublicPort,
	IReadOnlyList<AdbDeviceDto> Devices,
	string? LastError,
	DateTimeOffset? LastErrorAt,
	bool PreviousShutdownWasUnclean,
	int StaleTunnelsCleaned,
	bool Supported,
	string? UnsupportedReason)
{
	public static AdbSettingsProjection From(AdbSettings settings,
		AdbStatus status,
		IReadOnlyList<AdbDevice> devices,
		int activePublicPort)
		=> new(settings.Enabled,
			settings.ExecutablePath,
			status.ResolvedExecutablePath,
			status.ExecutableSource.ToString(),
			status.AdbVersion,
			status.ServerReachable,
			status.ServerStartedByMacroDeck,
			settings.UsbConnectionsEnabled,
			settings.DefaultDeviceSerial,
			activePublicPort,
			devices.Select(device => AdbDeviceDto.From(device, settings.DefaultDeviceSerial)).ToList(),
			status.LastFailureMessage,
			status.LastFailureAt,
			status.PreviousShutdownWasUnclean,
			status.StaleTunnelsCleaned,
			status.Supported,
			// AdbStatus carries no dedicated reason field: null here just means none was recorded, for
			// example because the integration is simply turned off rather than genuinely unsupported.
			status.Supported ? null : status.LastFailureMessage);
}
