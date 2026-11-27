using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Devices.Surfaces.InProcess;

/// <summary>The <see cref="IDeviceSession" /> an in-process provider renders and reports through.</summary>
internal sealed class InProcessDeviceSession : IDeviceSession
{
	private readonly Guid _deviceId;
	private readonly Func<IDeviceSurfaceService> _service;
	private readonly Lock _keyImageGate = new();
	private readonly Dictionary<(int Row, int Column), long> _keyImageRevisions = [];

	private int _closed;

	public InProcessDeviceSession(
		Guid deviceId,
		string providerDeviceId,
		Func<IDeviceSurfaceService> service,
		LayoutKeyImageFormat? keyImageFormat = null)
	{
		_deviceId = deviceId;
		_service = service;
		DeviceId = deviceId.ToString();
		ProviderDeviceId = providerDeviceId;
		KeyImageFormat = keyImageFormat;
	}

	public string DeviceId { get; }

	public string ProviderDeviceId { get; }

	public DeviceSurface CurrentSurface { get; private set; } = DeviceSurface.Empty;

	public event EventHandler<DeviceSurfaceChangedEventArgs>? SurfaceChanged;

	public event EventHandler<DeviceSessionClosedEventArgs>? Closed;

	public event EventHandler<DeviceKeyImageChangedEventArgs>? KeyImageChanged;

	public LayoutKeyImageFormat? KeyImageFormat { get; }

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
	{
		return _service().GetIconAsync(_deviceId, iconId, size, knownETag, cancellationToken);
	}

	public Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		return _service().GetWidgetIconAsync(_deviceId, widgetId, knownETag, cancellationToken);
	}

	public async Task<DeviceKeyImage?> GetKeyImageAsync(
		int row,
		int column,
		CancellationToken cancellationToken = default)
	{
		if (KeyImageFormat is null)
		{
			return null;
		}

		var image = await _service().GetKeyImageAsync(_deviceId, row, column, cancellationToken);
		if (image is not null)
		{
			lock (_keyImageGate)
			{
				var cell = (image.Row, image.Column);
				if (image.Revision > _keyImageRevisions.GetValueOrDefault(cell))
				{
					_keyImageRevisions[cell] = image.Revision;
				}
			}
		}

		return image;
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(_service().CloseAsync(_deviceId, null));
	}

	public void Apply(DeviceSurface surface)
	{
		CurrentSurface = surface;
		SurfaceChanged?.Invoke(this, new DeviceSurfaceChangedEventArgs(surface));
	}

	public void ApplyKeyImages(IReadOnlyList<DeviceKeyImage> images)
	{
		lock (_keyImageGate)
		{
			foreach (var image in images)
			{
				var cell = (image.Row, image.Column);
				if (Volatile.Read(ref _closed) != 0 || image.Revision <= _keyImageRevisions.GetValueOrDefault(cell))
				{
					continue;
				}

				_keyImageRevisions[cell] = image.Revision;
				KeyImageChanged?.Invoke(this, new DeviceKeyImageChangedEventArgs(image));
			}
		}
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
