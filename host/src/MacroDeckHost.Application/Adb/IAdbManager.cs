using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Adb;

public interface IAdbManager
{
	AdbStatus Status { get; }

	IReadOnlyList<AdbDevice> Devices { get; }

	AdbDevice? ResolveDevice(string? serialOrDefault);

	event EventHandler<AdbDeviceChange>? DeviceChanged;

	Task<Result<AdbFailureCode>> ExecuteAsync(AdbCommand command, CancellationToken cancellationToken);

	/// <summary>
	/// Runs a read-only command and returns what the device printed. Restricted to
	/// <see cref="AdbQueryCommand"/>: an action's output is not something callers get to depend on.
	/// </summary>
	Task<Result<string, AdbFailureCode>> QueryAsync(AdbQueryCommand command, CancellationToken cancellationToken);

	Task<Result<byte[], AdbFailureCode>> CaptureScreenshotAsync(
		string? serialOrDefault,
		CancellationToken cancellationToken);

	Task<AdbDeviceProperties?> GetPropertiesAsync(string? serialOrDefault, CancellationToken cancellationToken);

	Task ApplySettingsAsync(CancellationToken cancellationToken);

	Task<Result<AdbFailureCode>> RestartServerAsync(CancellationToken cancellationToken);

	Task RefreshNowAsync(CancellationToken cancellationToken);

	Task ShutdownAsync();
}
