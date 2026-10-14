using MacroDeck.Sdk.Companion;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="ICompanionDeviceApi" /> for testing a plugin's Companion device logic without a phone.
/// Access starts as <see cref="CompanionDeviceAccess.Available" /> with no devices. Connect devices with
/// <see cref="Connect" />, change what they report with <see cref="Report" />, and decide how requests and
/// screenshots end with <see cref="OnRequest" /> and <see cref="OnScreenshot" />. Events are raised synchronously
/// on the calling thread.
/// </summary>
public sealed class FakeCompanionDeviceApi : ICompanionDeviceApi
{
	private readonly Lock _gate = new();
	private readonly List<CompanionDevice> _devices = [];
	private readonly List<FakeCompanionDeviceCall> _calls = [];
	private CompanionDeviceAccess _access = CompanionDeviceAccess.Available;
	private long _nextReadySequence;

	/// <inheritdoc />
	public event EventHandler? AccessChanged;

	/// <inheritdoc />
	public event EventHandler<CompanionDeviceEventArgs>? DeviceReady;

	/// <inheritdoc />
	public event EventHandler<CompanionDeviceEventArgs>? DeviceChanged;

	/// <summary>
	/// Decides how <see cref="RequestAsync" /> ends for a connected device. Defaults to
	/// <see cref="CompanionDeviceCommandResult.Done" />.
	/// </summary>
	public Func<Guid, CompanionDeviceCommand, CompanionDeviceCommandResult>? OnRequest { get; set; }

	/// <summary>
	/// Decides how <see cref="CaptureScreenshotAsync" /> ends for a connected device. Defaults to
	/// <see cref="CompanionDeviceFailure.Unavailable" />.
	/// </summary>
	public Func<Guid, CompanionScreenshotMode, CompanionScreenshotResult>? OnScreenshot { get; set; }

	/// <inheritdoc />
	public CompanionDeviceAccess Access
	{
		get
		{
			lock (_gate)
			{
				return _access;
			}
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<CompanionDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				return _access == CompanionDeviceAccess.Available ? [.. _devices] : [];
			}
		}
	}

	/// <summary>Every send, request and screenshot the plugin made, in order, including refused ones.</summary>
	public IReadOnlyList<FakeCompanionDeviceCall> Calls
	{
		get
		{
			lock (_gate)
			{
				return [.. _calls];
			}
		}
	}

	/// <inheritdoc />
	public CompanionDevice? FindDevice(Guid deviceId)
	{
		return Devices.FirstOrDefault(device => device.Id == deviceId);
	}

	/// <summary>Changes <see cref="Access" /> and raises <see cref="AccessChanged" /> when it changed.</summary>
	public void SetAccess(CompanionDeviceAccess access)
	{
		lock (_gate)
		{
			if (_access == access)
			{
				return;
			}

			_access = access;
		}

		AccessChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Connects a device, or reconnects one, with a new <see cref="CompanionDevice.ReadySequence" />, and raises
	/// <see cref="DeviceChanged" /> and <see cref="DeviceReady" />.
	/// </summary>
	public CompanionDevice Connect(Guid deviceId, CompanionDeviceState? state = null)
	{
		CompanionDevice device;
		lock (_gate)
		{
			_devices.RemoveAll(existing => existing.Id == deviceId);
			device = new CompanionDevice(deviceId, ++_nextReadySequence, state ?? new CompanionDeviceState());
			_devices.Add(device);
		}

		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, device));
		DeviceReady?.Invoke(this, new CompanionDeviceEventArgs(deviceId, device));
		return device;
	}

	/// <summary>Replaces what a connected device reports and raises <see cref="DeviceChanged" />.</summary>
	/// <exception cref="InvalidOperationException">The device is not connected.</exception>
	public CompanionDevice Report(Guid deviceId, CompanionDeviceState state)
	{
		ArgumentNullException.ThrowIfNull(state);
		CompanionDevice device;
		lock (_gate)
		{
			var index = _devices.FindIndex(existing => existing.Id == deviceId);
			if (index < 0)
			{
				throw new InvalidOperationException($"Device {deviceId} is not connected.");
			}

			device = _devices[index] with { State = state };
			_devices[index] = device;
		}

		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, device));
		return device;
	}

	/// <summary>Disconnects a device and raises <see cref="DeviceChanged" /> with no device.</summary>
	public void Disconnect(Guid deviceId)
	{
		lock (_gate)
		{
			if (_devices.RemoveAll(existing => existing.Id == deviceId) == 0)
			{
				return;
			}
		}

		DeviceChanged?.Invoke(this, new CompanionDeviceEventArgs(deviceId, null));
	}

	/// <inheritdoc />
	public Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(FindDevice(deviceId) is not null);
	}

	/// <inheritdoc />
	public Task<bool> SendAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		Record(new FakeCompanionDeviceCall(deviceId, FakeCompanionDeviceCallKind.Send, command, null));
		return Task.FromResult(FindDevice(deviceId) is not null);
	}

	/// <inheritdoc />
	public Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		Record(new FakeCompanionDeviceCall(deviceId, FakeCompanionDeviceCallKind.Request, command, null));
		if (Refusal() is { } refused)
		{
			return Task.FromResult(CompanionDeviceCommandResult.Failed(refused));
		}

		return Task.FromResult(FindDevice(deviceId) is null
			? CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotConnected)
			: OnRequest?.Invoke(deviceId, command) ?? CompanionDeviceCommandResult.Done);
	}

	/// <inheritdoc />
	public Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default)
	{
		Record(new FakeCompanionDeviceCall(deviceId, FakeCompanionDeviceCallKind.Screenshot, null, mode));
		if (Refusal() is { } refused)
		{
			return Task.FromResult(CompanionScreenshotResult.Failed(refused));
		}

		return Task.FromResult(FindDevice(deviceId) is null
			? CompanionScreenshotResult.Failed(CompanionDeviceFailure.NotConnected)
			: OnScreenshot?.Invoke(deviceId, mode)
			?? CompanionScreenshotResult.Failed(CompanionDeviceFailure.Unavailable));
	}

	private CompanionDeviceFailure? Refusal()
	{
		return Access switch
		{
			CompanionDeviceAccess.Available => null,
			CompanionDeviceAccess.NotAllowed => CompanionDeviceFailure.NotAllowed,
			_ => CompanionDeviceFailure.NotConnected
		};
	}

	private void Record(FakeCompanionDeviceCall call)
	{
		lock (_gate)
		{
			_calls.Add(call);
		}
	}
}

public enum FakeCompanionDeviceCallKind
{
	Send,
	Request,
	Screenshot
}

/// <summary>One call a plugin made on a <see cref="FakeCompanionDeviceApi" />.</summary>
/// <param name="DeviceId">The device it named.</param>
/// <param name="Kind">Which operation it was.</param>
/// <param name="Command">The command of a send or request.</param>
/// <param name="ScreenshotMode">The mode of a screenshot.</param>
public sealed record FakeCompanionDeviceCall(
	Guid DeviceId,
	FakeCompanionDeviceCallKind Kind,
	CompanionDeviceCommand? Command,
	CompanionScreenshotMode? ScreenshotMode);
