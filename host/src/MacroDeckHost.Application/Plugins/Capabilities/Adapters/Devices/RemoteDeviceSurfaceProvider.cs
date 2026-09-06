using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;

/// <summary>
/// Serves a connected plugin's device sessions over <c>capability.invoke</c>, the remote half of
/// <see cref="IDeviceSurfaceProvider" />. Interactions and icon requests never come back as results
/// here - the plugin reports those through the <c>devices</c> host api.
/// </summary>
public sealed class RemoteDeviceSurfaceProvider : IDeviceSurfaceProvider
{
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly RemoteDeviceSessionRegistry _sessions;

	private readonly ConcurrentDictionary<string, string> _sessionIdsByDeviceId = new(StringComparer.Ordinal);

	public RemoteDeviceSurfaceProvider(
		string pluginId,
		IPluginCapabilityInvoker invoker,
		RemoteDeviceSessionRegistry sessions)
	{
		ProviderId = pluginId;
		_invoker = invoker;
		_sessions = sessions;
	}

	public string ProviderId { get; }

	public async Task<bool> OpenAsync(DeviceSurfaceSessionDescriptor session, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(session);

		if (!Guid.TryParse(session.DeviceId, out var deviceId))
		{
			return false;
		}

		// A fresh id per open, never the device id: a session.close or an interaction that belonged to a
		// previous session must not be able to address the one that replaced it.
		var sessionId = Guid.CreateVersion7().ToString();
		_sessions.Add(sessionId, ProviderId, deviceId);
		_sessionIdsByDeviceId[session.DeviceId] = sessionId;

		JsonElement? data;
		try
		{
			data = await InvokeAsync(CapabilityOperations.DeviceProvider.SessionOpen,
					new DeviceSessionOpenArguments
					{
						SessionId = sessionId,
						DeviceId = session.DeviceId,
						ProviderDeviceId = session.ProviderDeviceId
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch
		{
			Forget(session.DeviceId, sessionId);
			throw;
		}

		var result = data?.Deserialize<DeviceSessionOpenResult>(PluginProtocolJson.Options);
		if (result is null || !result.Accepted)
		{
			Forget(session.DeviceId, sessionId);
			return false;
		}

		return true;
	}

	public Task PushAsync(string deviceId, DeviceSurface surface, CancellationToken cancellationToken)
		=> _sessionIdsByDeviceId.TryGetValue(deviceId, out var sessionId)
			? InvokeAsync(CapabilityOperations.DeviceProvider.SessionSurface,
				new DeviceSessionSurfaceArguments
				{
					SessionId = sessionId, Surface = DeviceSurfaceMapper.ToDto(surface)
				},
				cancellationToken)
			: Task.CompletedTask;

	public async Task CloseAsync(string deviceId, string? reason, CancellationToken cancellationToken)
	{
		if (!_sessionIdsByDeviceId.TryRemove(deviceId, out var sessionId))
		{
			return;
		}

		_sessions.Remove(sessionId);

		await InvokeAsync(CapabilityOperations.DeviceProvider.SessionClose,
				new DeviceSessionCloseArguments { SessionId = sessionId, Reason = reason },
				cancellationToken)
			.ConfigureAwait(false);
	}

	private void Forget(string deviceId, string sessionId)
	{
		_sessions.Remove(sessionId);
		_sessionIdsByDeviceId.TryRemove(new KeyValuePair<string, string>(deviceId, sessionId));
	}

	private Task<JsonElement?> InvokeAsync(string operation, object arguments, CancellationToken cancellationToken)
		=> _invoker.InvokeAsync(ProviderId,
			new CapabilityInvokeRequest
			{
				Kind = CapabilityKinds.DeviceProvider,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				Arguments = arguments
			},
			cancellationToken);
}
