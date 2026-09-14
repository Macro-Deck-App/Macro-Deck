using MacroDeck.Sdk;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeckHost.Application.ScreenSavers;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class ScreenSaverProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IScreenSaverRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public ScreenSaverProviderHost(IScreenSaverRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<ScreenSaverProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IScreenSaverProvider provider)
		{
			return;
		}

		var context = new IntegrationScreenSaverProviderContext(integration.Id, _registry);

		try
		{
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Screensaver provider '{IntegrationId}' failed to start and provides no screensavers",
				integration.Id);
		}
	}

	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is IScreenSaverProvider)
		{
			await _registry.UnregisterAll(integration.Id, cancellationToken);
		}
	}
}
