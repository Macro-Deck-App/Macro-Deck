using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class FakeAdbManager : IAdbManager
{
	public AdbStatus Status { get; set; } = AdbStatus.Disabled;

	public IReadOnlyList<AdbDevice> Devices { get; set; } = [];

	public (AdbStatus Status, IReadOnlyList<AdbDevice> Devices)? NextSnapshot { get; set; }

	public Result<AdbFailureCode> RestartServerResult { get; set; } = Result.Ok<AdbFailureCode>();

	public List<AdbCommand> ExecutedCommands { get; } = [];

	public Result<AdbFailureCode> ExecuteResult { get; set; } = Result.Ok<AdbFailureCode>();

	/// <summary>Per-command outcome, for a caller that issues several commands and fails on one.</summary>
	public Func<AdbCommand, Result<AdbFailureCode>>? ExecuteResultFor { get; set; }

	public List<string?> CaptureScreenshotSerials { get; } = [];

	public Result<byte[], AdbFailureCode> CaptureScreenshotResult { get; set; } = Result.Ok<byte[], AdbFailureCode>([]);

	public List<string?> PropertiesSerials { get; } = [];

	public AdbDeviceProperties? PropertiesResult { get; set; }

	public int ApplySettingsCallCount { get; private set; }

	public int RestartServerCallCount { get; private set; }

	public event EventHandler<AdbDeviceChange>? DeviceChanged;

	public AdbDevice? ResolveDevice(string? serialOrDefault)
	{
		var serial = string.IsNullOrWhiteSpace(serialOrDefault) ? Status.DefaultDeviceSerial : serialOrDefault;
		return Devices.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
	}

	public Task<Result<AdbFailureCode>> ExecuteAsync(AdbCommand command, CancellationToken cancellationToken)
	{
		ExecutedCommands.Add(command);
		return Task.FromResult(ExecuteResultFor?.Invoke(command) ?? ExecuteResult);
	}

	public List<AdbQueryCommand> Queries { get; } = [];

	/// <summary>Scripted device output per query; anything unscripted answers empty.</summary>
	public Func<AdbQueryCommand, Result<string, AdbFailureCode>>? QueryResultFor { get; set; }

	public Task<Result<string, AdbFailureCode>> QueryAsync(
		AdbQueryCommand command,
		CancellationToken cancellationToken)
	{
		Queries.Add(command);
		return Task.FromResult(QueryResultFor?.Invoke(command) ?? Result.Ok<string, AdbFailureCode>(string.Empty));
	}

	public Task<Result<byte[], AdbFailureCode>> CaptureScreenshotAsync(string? serialOrDefault,
		CancellationToken cancellationToken)
	{
		CaptureScreenshotSerials.Add(serialOrDefault);
		return Task.FromResult(CaptureScreenshotResult);
	}

	public Task<AdbDeviceProperties?> GetPropertiesAsync(string? serialOrDefault, CancellationToken cancellationToken)
	{
		PropertiesSerials.Add(serialOrDefault);
		return Task.FromResult(PropertiesResult);
	}

	public Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		ApplySettingsCallCount++;
		ApplyNextSnapshot();
		return Task.CompletedTask;
	}

	public Task<Result<AdbFailureCode>> RestartServerAsync(CancellationToken cancellationToken)
	{
		RestartServerCallCount++;
		ApplyNextSnapshot();
		return Task.FromResult(RestartServerResult);
	}

	public Task RefreshNowAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public void RaiseDeviceChanged(AdbDeviceChange change) => DeviceChanged?.Invoke(this, change);

	private void ApplyNextSnapshot()
	{
		if (NextSnapshot is not { } snapshot)
		{
			return;
		}

		Status = snapshot.Status;
		Devices = snapshot.Devices;
	}
}
