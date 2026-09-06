using MacroDeck.Sdk;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Widgets;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Runs the widget-type-provider side of an in-process integration's lifecycle: hands a started provider
/// its context, and withdraws its types when it stops. Mirrors <see cref="FolderViewProviderHost" /> exactly -
/// see its remarks for why process exit is not a call site here either.
/// </summary>
public sealed class WidgetTypeProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IWidgetTypeRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public WidgetTypeProviderHost(IWidgetTypeRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<WidgetTypeProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IWidgetTypeProvider provider)
		{
			return;
		}

		var context = new IntegrationWidgetTypeProviderContext(integration.Id, _registry);

		try
		{
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Widget type provider '{IntegrationId}' failed to start and provides no widget types",
				integration.Id);
		}
	}

	/// <summary>
	/// Withdraws the integration's widget types from the live catalog. Widgets already placed with one of
	/// them keep their stored type and data: they draw nothing until the provider is back, rather than
	/// losing what they were configured with.
	/// </summary>
	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is IWidgetTypeProvider)
		{
			await _registry.UnregisterAll(integration.Id, cancellationToken);
		}
	}
}
