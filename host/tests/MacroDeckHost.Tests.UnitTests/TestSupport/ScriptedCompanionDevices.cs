using MacroDeck.Sdk.Companion;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class ScriptedCompanionDevices : ICompanionDeviceApi
{
	private readonly List<CompanionDevice> _devices = [];
	private long _sequence;

	public event EventHandler? AccessChanged
	{
		add { }
		remove { }
	}

	public event EventHandler<CompanionDeviceEventArgs>? DeviceReady;

	public event EventHandler<CompanionDeviceEventArgs>? DeviceChanged;

	public CompanionDeviceAccess Access => CompanionDeviceAccess.Available;

	public IReadOnlyList<CompanionDevice> Devices => [.. _devices];

	public List<(string Operation, Guid DeviceId, CompanionDeviceCommand? Command)> Calls { get; } = [];

	public CompanionDeviceCommandResult RequestResult { get; set; } = CompanionDeviceCommandResult.Done;

	public CompanionScreenshotResult ScreenshotResult { get; set; } =
		CompanionScreenshotResult.Failed(CompanionDeviceFailure.Unavailable);

	public int SubscriberCount => (DeviceReady?.GetInvocationList().Length ?? 0)
		+ (DeviceChanged?.GetInvocationList().Length ?? 0);

	public CompanionDevice Connect(Guid deviceId, CompanionDeviceState state)
	{
		_devices.RemoveAll(device => device.Id == deviceId);
		var device = new CompanionDevice(deviceId, ++_sequence, state);
		_devices.Add(device);
		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, device));
		DeviceReady?.Invoke(this, new CompanionDeviceEventArgs(deviceId, device));
		return device;
	}

	public void Report(Guid deviceId, CompanionDeviceState state)
	{
		var index = _devices.FindIndex(device => device.Id == deviceId);
		_devices[index] = _devices[index] with { State = state };
		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, _devices[index]));
	}

	public void Disconnect(Guid deviceId)
	{
		_devices.RemoveAll(device => device.Id == deviceId);
		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, null));
	}

	public CompanionDevice? FindDevice(Guid deviceId)
	{
		return _devices.FirstOrDefault(device => device.Id == deviceId);
	}

	public Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(FindDevice(deviceId) is not null);
	}

	public Task<bool> SendAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		Calls.Add(("send", deviceId, command));
		return Task.FromResult(FindDevice(deviceId) is not null);
	}

	public Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		Calls.Add(("request", deviceId, command));
		return Task.FromResult(RequestResult);
	}

	public Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default)
	{
		Calls.Add(($"screenshot:{mode}", deviceId, null));
		return Task.FromResult(ScreenshotResult);
	}
}
