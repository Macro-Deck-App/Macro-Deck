using MacroDeck.Sdk;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class BatteryProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IBatteryRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public BatteryProviderHost(IBatteryRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<BatteryProviderHost>();
	}

	public async Task StartAsync(
		IIntegration integration,
		string integrationName,
		CancellationToken cancellationToken = default)
	{
		if (integration is not IBatteryProvider provider || integration is RemotePluginIntegration)
		{
			return;
		}

		var providerName = string.IsNullOrWhiteSpace(provider.ProviderName) ? integrationName : provider.ProviderName;
		_registry.SetProviderName(integration.Id, providerName);
		var context = new IntegrationBatteryProviderContext(integration.Id, _registry);

		try
		{
			// Provider code is free to block its calling thread before returning a Task, and it must not
			// block whoever started it.
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Battery provider '{IntegrationId}' failed to start and provides no devices",
				integration.Id);
		}
	}

	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IBatteryProvider provider || integration is RemotePluginIntegration)
		{
			return;
		}

		try
		{
			await Task.Run(() => provider.ShutdownAsync(cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception, "Battery provider '{IntegrationId}' failed to stop cleanly", integration.Id);
		}

		_registry.RemoveAll(integration.Id);
	}
}
