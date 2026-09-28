using MacroDeck.Sdk;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.VideoStreams;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class VideoStreamProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IVideoStreamSessionBroker _broker;
	private readonly ILogger _logger;
	private readonly VideoStreamProviderRegistry _registry;
	private readonly TimeProvider _timeProvider;

	public VideoStreamProviderHost(VideoStreamProviderRegistry registry,
		IVideoStreamSessionBroker broker,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_registry = registry;
		_broker = broker;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<VideoStreamProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IVideoStreamIntegration videoStreams || integration is RemotePluginIntegration)
		{
			return;
		}

		var context = new IntegrationVideoStreamProviderContext(integration.Id, _registry, _broker);

		try
		{
			await Task.Run(() => videoStreams.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Video stream integration '{IntegrationId}' failed to start and provides no video streams",
				integration.Id);
		}
	}

	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is IVideoStreamIntegration and not RemotePluginIntegration)
		{
			await _registry.WithdrawInProcessAsync(integration.Id);
		}
	}
}
