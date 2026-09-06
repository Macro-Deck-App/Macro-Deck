namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetAdbSettingsResponse
{
	public bool Enabled { get; set; }

	public string? ExecutablePath { get; set; }

	public string? ResolvedExecutablePath { get; set; }

	public string ExecutableSource { get; set; } = string.Empty;

	public string? AdbVersion { get; set; }

	public bool ServerReachable { get; set; }

	public bool ServerStartedByMacroDeck { get; set; }

	public bool UsbConnectionsEnabled { get; set; }

	public string? DefaultDeviceSerial { get; set; }

	public int ActivePublicPort { get; set; }

	public IReadOnlyList<int> DeviceSidePortCandidates { get; set; } = [];

	public IReadOnlyList<AdbDeviceDto> Devices { get; set; } = [];

	public string? LastError { get; set; }

	public DateTimeOffset? LastErrorAt { get; set; }

	public bool PreviousShutdownWasUnclean { get; set; }

	public int StaleTunnelsCleaned { get; set; }

	public bool Supported { get; set; }

	public string? UnsupportedReason { get; set; }
}
