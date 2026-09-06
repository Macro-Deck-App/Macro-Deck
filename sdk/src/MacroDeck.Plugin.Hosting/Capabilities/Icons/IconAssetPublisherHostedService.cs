using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.Icons;

/// <summary>
/// Pushes the plugin's icon over the <c>asset.*</c> pipeline every time the connection becomes usable,
/// mirroring <c>IntegrationLifecycleHostedService</c>'s subscription to <see cref="PluginConnectionState.Connected" />.
///
/// <para>
/// Fire-and-forget by necessity: <see cref="PluginConnectionState.Connected" /> is a synchronous
/// <see cref="EventHandler{TEventArgs}" />, raised from inside the handshake, and the handshake must
/// not block on an upload that itself depends on the connection the handshake is still completing. A
/// failed or slow upload is logged and simply leaves the host's registrar to time out waiting for the
/// icon and register the plugin without one - see <c>RemotePluginIntegrationRegistrar</c>'s remarks on
/// that budget.
/// </para>
/// </summary>
internal sealed class IconAssetPublisherHostedService(
	IconAssetSource iconSource,
	IPluginAssetUploader uploader,
	PluginConnectionState connectionState,
	ILogger logger) : IHostedService
{
	private readonly ILogger _logger = logger.ForContext<IconAssetPublisherHostedService>();

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (iconSource.HasIcon)
		{
			connectionState.Connected += OnConnected;
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		connectionState.Connected -= OnConnected;
		return Task.CompletedTask;
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e) => _ = PublishAsync();

	private async Task PublishAsync()
	{
		try
		{
			await uploader.UploadAsync(AssetKinds.Icon, iconSource.MimeType, iconSource.Bytes, CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.IconPublishFailed(exception);
		}
	}
}
