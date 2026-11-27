using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;

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
	private readonly Lock _keyImageGate = new();
	private readonly Dictionary<(int Row, int Column), long> _keyImageRevisions = [];
	private readonly CancellationTokenSource _closing = new();
	private readonly ConcurrentDictionary<string, byte> _pendingTransfers = new(StringComparer.Ordinal);

	private int _closed;

	public RemoteDeviceSession(
		string sessionId,
		string deviceId,
		string providerDeviceId,
		IHostInvoker invoker,
		IPluginHostAssetReceiver assets,
		LayoutKeyImageFormat? keyImageFormat = null)
	{
		_sessionId = sessionId;
		_invoker = invoker;
		_assets = assets;
		DeviceId = deviceId;
		ProviderDeviceId = providerDeviceId;
		KeyImageFormat = keyImageFormat;
	}

	public string DeviceId { get; }

	public string ProviderDeviceId { get; }

	public DeviceSurface CurrentSurface { get; private set; } = DeviceSurface.Empty;

	public LayoutKeyImageFormat? KeyImageFormat { get; }

	public event EventHandler<DeviceSurfaceChangedEventArgs>? SurfaceChanged;

	public event EventHandler<DeviceKeyImageChangedEventArgs>? KeyImageChanged;

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

	public async Task<DeviceKeyImage?> GetKeyImageAsync(
		int row,
		int column,
		CancellationToken cancellationToken = default)
	{
		if (KeyImageFormat is null)
		{
			return null;
		}

		JsonElement? data;
		try
		{
			data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Devices,
					HostOperations.Devices.KeyImage,
					new DevicesKeyImageArguments { SessionId = _sessionId, Row = row, Column = column },
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
			when (exception.Code is ProtocolErrorCodes.CapabilityUnsupported
				or ProtocolErrorCodes.CapabilityUnavailable)
		{
			// A host that predates key-image fails it as unknown; the contract promises null, never a throw.
			return null;
		}

		var result = data?.Deserialize<DevicesKeyImageResult>(PluginProtocolJson.Options);
		if (result is null)
		{
			return null;
		}

		var content = await ContentAsync(result.Content, result.TransferId, cancellationToken).ConfigureAwait(false);
		if (content is null)
		{
			return null;
		}

		lock (_keyImageGate)
		{
			var cell = (result.Row, result.Column);
			if (result.Revision > _keyImageRevisions.GetValueOrDefault(cell))
			{
				_keyImageRevisions[cell] = result.Revision;
			}
		}

		return new DeviceKeyImage
		{
			Row = result.Row,
			Column = result.Column,
			Revision = result.Revision,
			ContentType = result.ContentType,
			Content = content
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

		RaiseClosed(null);
	}

	public void Apply(DeviceSurfaceDto surface)
	{
		var applied = DeviceSurfaceMapper.ToSurface(surface);
		CurrentSurface = applied;
		SurfaceChanged?.Invoke(this, new DeviceSurfaceChangedEventArgs(applied));
	}

	public void ApplyKeyImages(IReadOnlyList<DeviceSessionKeyImageDto> images)
	{
		foreach (var image in images)
		{
			if (image.Content is { } content)
			{
				RaiseKeyImage(image, content);
			}
			else if (image.TransferId is { Length: > 0 } transferId)
			{
				_ = ReceiveKeyImageAsync(image, transferId);
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

		_closing.Cancel();
		foreach (var transferId in _pendingTransfers.Keys)
		{
			_assets.Release(transferId);
		}

		Closed?.Invoke(this, new DeviceSessionClosedEventArgs(reason));
	}

	// Not awaited by the push: the transfer arrives over the same connection the push reply goes out on.
	private async Task ReceiveKeyImageAsync(DeviceSessionKeyImageDto image, string transferId)
	{
		try
		{
			if (await ContentAsync(null, transferId, CancellationToken.None).ConfigureAwait(false) is { } content)
			{
				RaiseKeyImage(image, content);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// A failed transfer or a throwing handler loses one picture; a newer revision replaces it.
		}
	}

	private async Task<byte[]?> ContentAsync(byte[]? inline, string? transferId, CancellationToken cancellationToken)
	{
		if (inline is not null)
		{
			return inline;
		}

		if (transferId is not { Length: > 0 })
		{
			return null;
		}

		// Released on close rather than left to the receiver, which holds an unclaimed transfer until reconnect.
		_pendingTransfers.TryAdd(transferId, 0);
		using var awaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
		try
		{
			var transfer = await _assets.AwaitAsync(transferId, awaiting.Token).ConfigureAwait(false);
			return transfer.Accepted ? transfer.Content : null;
		}
		catch (OperationCanceledException) when (_closing.IsCancellationRequested)
		{
			_assets.Release(transferId);
			return null;
		}
		finally
		{
			_pendingTransfers.TryRemove(transferId, out _);
		}
	}

	private void RaiseKeyImage(DeviceSessionKeyImageDto image, byte[] content)
	{
		// Raised under the gate: a picture that arrived over a transfer must not overtake a newer inline one.
		lock (_keyImageGate)
		{
			var cell = (image.Row, image.Column);
			if (Volatile.Read(ref _closed) != 0 || image.Revision <= _keyImageRevisions.GetValueOrDefault(cell))
			{
				return;
			}

			_keyImageRevisions[cell] = image.Revision;
			KeyImageChanged?.Invoke(this,
				new DeviceKeyImageChangedEventArgs(new DeviceKeyImage
				{
					Row = image.Row,
					Column = image.Column,
					Revision = image.Revision,
					ContentType = image.ContentType,
					Content = content
				}));
		}
	}
}
