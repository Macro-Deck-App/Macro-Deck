using MacroDeck.Sdk.Devices;

namespace MacroDeckHost.Application.Devices.Surfaces.InProcess;

/// <summary>The <see cref="IDeviceSession" /> an in-process provider renders and reports through.</summary>
internal sealed class InProcessDeviceSession : IDeviceSession
{
	private readonly Guid _deviceId;
	private readonly Func<IDeviceSurfaceService> _service;

	private int _closed;

	public InProcessDeviceSession(Guid deviceId, string providerDeviceId, Func<IDeviceSurfaceService> service)
	{
		_deviceId = deviceId;
		_service = service;
		DeviceId = deviceId.ToString();
		ProviderDeviceId = providerDeviceId;
	}

	public string DeviceId { get; }

	public string ProviderDeviceId { get; }

	public DeviceSurface CurrentSurface { get; private set; } = DeviceSurface.Empty;

	public event EventHandler<DeviceSurfaceChangedEventArgs>? SurfaceChanged;

	public event EventHandler<DeviceSessionClosedEventArgs>? Closed;

	public async Task<DeviceInteractionResult> SendInteractionAsync(
		DeviceInteraction interaction,
		CancellationToken cancellationToken = default)
	{
		var outcome = await _service().SubmitInteractionAsync(_deviceId, interaction, cancellationToken);
		return outcome.ToResult();
	}

	public Task<DeviceIconImage?> GetIconAsync(
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
		=> _service().GetIconAsync(_deviceId, iconId, size, knownETag, cancellationToken);

	public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
		=> _service().GetWidgetIconAsync(_deviceId, widgetId, knownETag, cancellationToken);

	public ValueTask DisposeAsync()
	{
		return new ValueTask(_service().CloseAsync(_deviceId, reason: null));
	}

	public void Apply(DeviceSurface surface)
	{
		CurrentSurface = surface;
		SurfaceChanged?.Invoke(this, new DeviceSurfaceChangedEventArgs(surface));
	}

	/// <summary>Raised once per session however the close was reached - a provider disposing the session
	/// and the host closing it must not both reach the provider.</summary>
	public void RaiseClosed(string? reason)
	{
		if (Interlocked.Exchange(ref _closed, 1) != 0)
		{
			return;
		}

		Closed?.Invoke(this, new DeviceSessionClosedEventArgs(reason));
	}
}
