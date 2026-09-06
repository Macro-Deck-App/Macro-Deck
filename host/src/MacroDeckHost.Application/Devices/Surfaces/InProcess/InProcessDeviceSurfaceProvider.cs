using System.Collections.Concurrent;
using MacroDeck.Sdk.Devices;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces.InProcess;

/// <summary>
/// Serves an in-process integration's <see cref="IDeviceProvider" /> as a surface provider: it hands
/// the provider an <see cref="IDeviceSession" /> object and turns host pushes into events on it. A
/// provider that leaves <see cref="IDeviceProvider.OnSessionOpenedAsync" /> at its default no-op simply
/// never renders, which is why the open is reported as accepted only once the provider has been given
/// the session.
/// </summary>
public sealed class InProcessDeviceSurfaceProvider : IDeviceSurfaceProvider
{
	private readonly IDeviceProvider _provider;
	private readonly Func<IDeviceSurfaceService> _service;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<string, InProcessDeviceSession> _sessions = new(StringComparer.Ordinal);

	public InProcessDeviceSurfaceProvider(
		string providerId,
		IDeviceProvider provider,
		Func<IDeviceSurfaceService> service,
		ILogger logger)
	{
		ProviderId = providerId;
		_provider = provider;
		_service = service;
		_logger = logger.ForContext<InProcessDeviceSurfaceProvider>();
	}

	public string ProviderId { get; }

	public async Task<bool> OpenAsync(DeviceSurfaceSessionDescriptor session, CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(session.DeviceId, out var deviceId))
		{
			return false;
		}

		var opened = new InProcessDeviceSession(deviceId, session.ProviderDeviceId, _service);
		_sessions[session.DeviceId] = opened;

		await _provider.OnSessionOpenedAsync(opened, cancellationToken);
		return true;
	}

	public Task PushAsync(string deviceId, DeviceSurface surface, CancellationToken cancellationToken)
	{
		if (_sessions.TryGetValue(deviceId, out var session))
		{
			session.Apply(surface);
		}

		return Task.CompletedTask;
	}

	public async Task CloseAsync(string deviceId, string? reason, CancellationToken cancellationToken)
	{
		if (!_sessions.TryRemove(deviceId, out var session))
		{
			return;
		}

		try
		{
			session.RaiseClosed(reason);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "A device provider threw while its session for {DeviceId} closed", deviceId);
		}
	}
}
