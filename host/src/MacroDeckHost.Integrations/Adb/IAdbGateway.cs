namespace MacroDeckHost.Integrations.Adb;

public interface IAdbGateway
{
	bool IsEnabled { get; }

	IReadOnlyList<AdbGatewayDevice> Devices { get; }

	string? DefaultDeviceSerial { get; }

	event EventHandler<AdbGatewayDeviceChange>? DeviceChanged;

	Task<AdbGatewayResult> SendKeyAsync(string? serial, AdbGatewayKey key, CancellationToken cancellationToken);

	Task<AdbGatewayResult> StartAppAsync(string? serial, string package, CancellationToken cancellationToken);

	Task<AdbGatewayResult> ForceStopAppAsync(string? serial, string package, CancellationToken cancellationToken);

	Task<AdbGatewayResult> OpenUriAsync(string? serial, string uri, CancellationToken cancellationToken);

	Task<AdbGatewayResult> InputTextAsync(string? serial, string text, CancellationToken cancellationToken);

	Task<AdbGatewayResult> TapAsync(string? serial, int x, int y, CancellationToken cancellationToken);

	Task<AdbGatewayResult> SwipeAsync(string? serial,
		int x1,
		int y1,
		int x2,
		int y2,
		int durationMs,
		CancellationToken cancellationToken);

	Task<AdbGatewayResult> RebootAsync(string? serial, AdbGatewayRebootMode mode, CancellationToken cancellationToken);

	Task<(AdbGatewayResult Result, byte[]? Png)> CaptureScreenshotAsync(string? serial,
		CancellationToken cancellationToken);

	Task<AdbGatewayProperties?> GetPropertiesAsync(string? serial, CancellationToken cancellationToken);
}
