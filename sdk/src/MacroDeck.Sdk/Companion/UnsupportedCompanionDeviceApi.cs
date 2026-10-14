namespace MacroDeck.Sdk.Companion;

/// <summary>
/// The <see cref="ICompanionDeviceApi" /> of a context that offers no Companion devices: access
/// <see cref="CompanionDeviceAccess.Unsupported" />, no devices, no events, and every operation fails as not connected.
/// </summary>
public sealed class UnsupportedCompanionDeviceApi : ICompanionDeviceApi
{
	public static readonly UnsupportedCompanionDeviceApi Instance = new();

	private UnsupportedCompanionDeviceApi()
	{
	}

	public CompanionDeviceAccess Access => CompanionDeviceAccess.Unsupported;

	public IReadOnlyList<CompanionDevice> Devices => [];

	public event EventHandler? AccessChanged
	{
		add { }
		remove { }
	}

	public event EventHandler<CompanionDeviceEventArgs>? DeviceReady
	{
		add { }
		remove { }
	}

	public event EventHandler<CompanionDeviceEventArgs>? DeviceChanged
	{
		add { }
		remove { }
	}

	public CompanionDevice? FindDevice(Guid deviceId)
	{
		return null;
	}

	public Task<bool> WaitForDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(false);
	}

	public Task<bool> SendAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(false);
	}

	public Task<CompanionDeviceCommandResult> RequestAsync(
		Guid deviceId,
		CompanionDeviceCommand command,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(CompanionDeviceCommandResult.Failed(CompanionDeviceFailure.NotConnected));
	}

	public Task<CompanionScreenshotResult> CaptureScreenshotAsync(
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(CompanionScreenshotResult.Failed(CompanionDeviceFailure.NotConnected));
	}
}
