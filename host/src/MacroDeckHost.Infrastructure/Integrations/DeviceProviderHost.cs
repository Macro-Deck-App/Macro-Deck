using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Runs the device-provider side of an in-process integration's lifecycle: hands a started provider its
/// context, and withdraws its devices when it stops.
/// </summary>
/// <remarks>
/// Process exit is deliberately not a call site. Provider presence lives only in memory, so a device
/// cannot outlive the process that tracked it, and an extra shutdown pass there would only add a way for
/// quitting to hang.
/// </remarks>
public sealed class DeviceProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IPluginDeviceRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public DeviceProviderHost(IPluginDeviceRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<DeviceProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IDeviceProvider provider)
		{
			return;
		}

		var context = new IntegrationDeviceProviderContext(integration.Id, _registry);

		try
		{
			// Same treatment IntegrationInitializer gives InitializeAsync: provider code is free to block
			// its calling thread before returning a Task, and it must not block whoever started it.
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Device provider '{IntegrationId}' failed to start and provides no devices",
				integration.Id);
		}
	}

	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IDeviceProvider provider)
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
			_logger.Error(exception, "Device provider '{IntegrationId}' failed to stop cleanly", integration.Id);
		}

		// Its devices are withdrawn either way: a provider that failed to stop is no more able to serve
		// them than one that stopped cleanly. They are retained, so reconnecting is still a reuse.
		await _registry.UnregisterAllAsync(integration.Id, cancellationToken);
	}
}
