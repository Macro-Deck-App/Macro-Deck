using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// The <see cref="IDeviceSession" /> a plugin's provider is handed: surfaces arrive as
/// <c>session.surface</c> pushes and are raised as events, and everything the provider sends back goes
/// out over <c>host.invoke</c> against <see cref="HostApis.Devices" />. Icon bytes are reassembled from
/// the <c>host.asset.*</c> channel behind <see cref="GetIconAsync" />, so a provider never sees a chunk.
/// </summary>
internal sealed class RemoteDeviceSession : IDeviceSession
{
	private readonly string _sessionId;
	private readonly IHostInvoker _invoker;
	private readonly IPluginHostAssetReceiver _assets;

	private int _closed;

	public RemoteDeviceSession(
		string sessionId,
		string deviceId,
		string providerDeviceId,
		IHostInvoker invoker,
		IPluginHostAssetReceiver assets)
	{
		_sessionId = sessionId;
		_invoker = invoker;
		_assets = assets;
		DeviceId = deviceId;
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
		ArgumentNullException.ThrowIfNull(interaction);

		var data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
				HostOperations.Devices.Interaction,
				DeviceSurfaceMapper.ToArguments(_sessionId, interaction),
				cancellationToken)
			.ConfigureAwait(false);

		// A host that answers with nothing at all took the interaction: only a refusal carries a reason.
		var result = data?.Deserialize<DevicesInteractionResult>(PluginProtocolJson.Options);
		if (result is null)
		{
			return DeviceInteractionResult.Accepted;
		}

		if (!result.Accepted)
		{
			return DeviceInteractionResult.Rejected(result.ReasonCode ?? DeviceSessionReasons.WidgetNotOnSurface);
		}

		return result.Unsupported ? DeviceInteractionResult.NotSupported : DeviceInteractionResult.Accepted;
	}

	public async Task<DeviceIconImage?> GetIconAsync(
		string iconId,
		int? size = null,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		JsonElement? data;
		try
		{
			data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
					HostOperations.Devices.Icon,
					new DevicesIconArguments
					{
						SessionId = _sessionId, IconId = iconId, Size = size, KnownETag = knownETag
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
			when (string.Equals(exception.Code, ProtocolErrorCodes.AssetTooLarge, StringComparison.Ordinal))
		{
			// Reported the same way an in-process session reports it, so a provider branches on one code
			// rather than on which side of the transport it happens to run.
			throw new DeviceSessionException(DeviceSessionReasons.IconTooLarge, exception.Message, exception);
		}

		var result = data?.Deserialize<DevicesIconResult>(PluginProtocolJson.Options);
		if (result is null)
		{
			return null;
		}

		if (result.NotModified)
		{
			return new DeviceIconImage
			{
				IconId = iconId,
				ContentType = result.ContentType,
				ETag = result.ETag,
				Content = ReadOnlyMemory<byte>.Empty,
				NotModified = true
			};
		}

		if (result.TransferId is not { Length: > 0 } transferId)
		{
			return null;
		}

		// Awaited only now, after the reply that names the transfer - the transfer itself may already have
		// arrived, which is why the receiver holds a completed transfer until it is claimed.
		var transfer = await _assets.AwaitAsync(transferId, cancellationToken).ConfigureAwait(false);
		if (!transfer.Accepted)
		{
			return null;
		}

		return new DeviceIconImage
		{
			IconId = iconId,
			ContentType = result.ContentType,
			ETag = result.ETag,
			Content = transfer.Content,
			NotModified = false
		};
	}

	public async Task<DeviceWidgetIconImage?> GetWidgetIconAsync(
		string widgetId,
		string? knownETag = null,
		CancellationToken cancellationToken = default)
	{
		var data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
				HostOperations.Devices.WidgetIcon,
				new DevicesWidgetIconArguments { SessionId = _sessionId, WidgetId = widgetId, KnownETag = knownETag },
				cancellationToken)
			.ConfigureAwait(false);

		var result = data?.Deserialize<DevicesWidgetIconResult>(PluginProtocolJson.Options);
		if (result is null)
		{
			return null;
		}

		if (result.NotModified)
		{
			return new DeviceWidgetIconImage
			{
				WidgetId = widgetId,
				ContentType = result.ContentType,
				ETag = result.ETag,
				Content = ReadOnlyMemory<byte>.Empty,
				NotModified = true
			};
		}

		if (result.TransferId is not { Length: > 0 } transferId)
		{
			return null;
		}

		// Awaited only now, after the reply that names the transfer - the transfer itself may already have
		// arrived, which is why the receiver holds a completed transfer until it is claimed.
		var transfer = await _assets.AwaitAsync(transferId, cancellationToken).ConfigureAwait(false);
		if (!transfer.Accepted)
		{
			return null;
		}

		return new DeviceWidgetIconImage
		{
			WidgetId = widgetId,
			ContentType = result.ContentType,
			ETag = result.ETag,
			Content = transfer.Content,
			NotModified = false
		};
	}

	/// <summary>
	/// Ends the session on both sides: the host is told to close it, exactly as an in-process session's
	/// dispose does, so a provider that stops serving a device is not left being pushed to. The host's
	/// own <c>session.close</c> normally raises <see cref="Closed" />; a host that cannot be reached
	/// leaves that to the local raise below, and either way it happens once.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		try
		{
			await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
					HostOperations.Devices.Close,
					new DevicesCloseArguments { SessionId = _sessionId },
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// A session the host has already forgotten, or a connection that is gone, still ends here.
		}

		RaiseClosed(reason: null);
	}

	public void Apply(DeviceSurfaceDto surface)
	{
		var applied = DeviceSurfaceMapper.ToSurface(surface);
		CurrentSurface = applied;
		SurfaceChanged?.Invoke(this, new DeviceSurfaceChangedEventArgs(applied));
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
