using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;

/// <summary>
/// Serves a connected plugin's device sessions over <c>capability.invoke</c>, the remote half of
/// <see cref="IDeviceSurfaceProvider" />. Interactions and icon requests never come back as results
/// here - the plugin reports those through the <c>devices</c> host api.
/// </summary>
public sealed class RemoteDeviceSurfaceProvider : IDeviceSurfaceProvider
{
	// Pictures ride session.key-images inline up to this much content per invoke, which keeps the base64
	// message well inside ProtocolLimits.MaxMessageBytes; a larger picture goes over host.asset instead.
	public const int MaxInlineKeyImageBytes = 128 * 1024;

	private const int KeyImagesMinimumVersion = 3;

	private readonly IPluginCapabilityInvoker _invoker;
	private readonly RemoteDeviceSessionRegistry _sessions;
	private readonly int _negotiatedVersion;
	private readonly IPluginHostAssetSender? _assets;

	private readonly ConcurrentDictionary<string, string> _sessionIdsByDeviceId = new(StringComparer.Ordinal);

	public RemoteDeviceSurfaceProvider(
		string pluginId,
		IPluginCapabilityInvoker invoker,
		RemoteDeviceSessionRegistry sessions,
		int negotiatedVersion = 2,
		IPluginHostAssetSender? assets = null)
	{
		ProviderId = pluginId;
		_invoker = invoker;
		_sessions = sessions;
		_negotiatedVersion = negotiatedVersion;
		_assets = assets;
	}

	public string ProviderId { get; }

	public bool SupportsKeyImages => _negotiatedVersion >= KeyImagesMinimumVersion;

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
						ProviderDeviceId = session.ProviderDeviceId,
						KeyImages = SupportsKeyImages ? LayoutDescriptorMapper.ToDto(session.KeyImages) : null
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
	{
		return _sessionIdsByDeviceId.TryGetValue(deviceId, out var sessionId)
			? InvokeAsync(CapabilityOperations.DeviceProvider.SessionSurface,
				new DeviceSessionSurfaceArguments
				{
					SessionId = sessionId, Surface = DeviceSurfaceMapper.ToDto(surface)
				},
				cancellationToken)
			: Task.CompletedTask;
	}

	public async Task PushKeyImagesAsync(
		string deviceId,
		IReadOnlyList<DeviceKeyImage> images,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(images);

		// A provider below version 3 does not know the operation and must never be sent it.
		if (!SupportsKeyImages || !_sessionIdsByDeviceId.TryGetValue(deviceId, out var sessionId))
		{
			return;
		}

		var batch = new List<DeviceSessionKeyImageDto>();
		var transfers = new List<(string TransferId, DeviceKeyImage Image)>();
		var inlineBytes = 0;

		foreach (var image in images)
		{
			if (image.Content.Length > MaxInlineKeyImageBytes)
			{
				if (_assets is null)
				{
					continue;
				}

				var transferId = Guid.CreateVersion7().ToString();
				transfers.Add((transferId, image));
				batch.Add(ToDto(image, null, transferId));
				continue;
			}

			if (batch.Count > 0 && inlineBytes + image.Content.Length > MaxInlineKeyImageBytes)
			{
				await SendKeyImagesAsync(sessionId, batch, transfers, cancellationToken).ConfigureAwait(false);
				batch = [];
				transfers = [];
				inlineBytes = 0;
			}

			batch.Add(ToDto(image, image.Content.ToArray(), null));
			inlineBytes += image.Content.Length;
		}

		if (batch.Count > 0)
		{
			await SendKeyImagesAsync(sessionId, batch, transfers, cancellationToken).ConfigureAwait(false);
		}
	}

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

	// The push goes first and the transfers after it: the plugin answers the push at once and only then
	// waits for each transfer by id, so a large picture never holds up the reply that announces it.
	private async Task SendKeyImagesAsync(
		string sessionId,
		IReadOnlyList<DeviceSessionKeyImageDto> batch,
		IReadOnlyList<(string TransferId, DeviceKeyImage Image)> transfers,
		CancellationToken cancellationToken)
	{
		await InvokeAsync(CapabilityOperations.DeviceProvider.SessionKeyImages,
				new DeviceSessionKeyImagesArguments { SessionId = sessionId, Images = batch },
				cancellationToken)
			.ConfigureAwait(false);

		foreach (var (transferId, image) in transfers)
		{
			await _assets!.SendAsync(ProviderId,
					transferId,
					AssetKinds.KeyImage,
					image.ContentType,
					image.Content,
					AssetContentHash.Compute(image.Content.Span),
					cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private static DeviceSessionKeyImageDto ToDto(DeviceKeyImage image, byte[]? content, string? transferId)
	{
		return new DeviceSessionKeyImageDto
		{
			Row = image.Row,
			Column = image.Column,
			Revision = image.Revision,
			ContentType = image.ContentType,
			Content = content,
			TransferId = transferId
		};
	}

	private void Forget(string deviceId, string sessionId)
	{
		_sessions.Remove(sessionId);
		_sessionIdsByDeviceId.TryRemove(new KeyValuePair<string, string>(deviceId, sessionId));
	}

	private Task<JsonElement?> InvokeAsync(string operation, object arguments, CancellationToken cancellationToken)
	{
		return _invoker.InvokeAsync(ProviderId,
			new CapabilityInvokeRequest
			{
				Kind = CapabilityKinds.DeviceProvider,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				Arguments = arguments
			},
			cancellationToken);
	}
}
