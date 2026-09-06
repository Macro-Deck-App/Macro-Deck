using MacroDeck.Sdk;
using MacroDeck.Sdk.FolderViews;
using MacroDeckHost.Application.FolderViews;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

/// <summary>
/// Runs the folder-view-provider side of an in-process integration's lifecycle: hands a started provider
/// its context, and withdraws its views when it stops. Mirrors <see cref="LayoutProviderHost" /> exactly -
/// see its remarks for why process exit is not a call site here either.
/// </summary>
public sealed class FolderViewProviderHost
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly IFolderViewRegistry _registry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public FolderViewProviderHost(IFolderViewRegistry registry, TimeProvider timeProvider, ILogger logger)
	{
		_registry = registry;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<FolderViewProviderHost>();
	}

	public async Task StartAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is not IFolderViewProvider provider)
		{
			return;
		}

		var context = new IntegrationFolderViewProviderContext(integration.Id, _registry);

		try
		{
			await Task.Run(() => provider.InitializeAsync(context, cancellationToken), CancellationToken.None)
				.WaitAsync(_timeout, _timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Error(exception,
				"Folder view provider '{IntegrationId}' failed to start and provides no folder views",
				integration.Id);
		}
	}

	/// <summary>
	/// Withdraws the integration's folder views from the live catalog. Folders that selected one keep
	/// their stored view id and configuration: they render the placeholder until the provider is back,
	/// rather than silently reverting to an empty widget grid.
	/// </summary>
	public async Task StopAsync(IIntegration integration, CancellationToken cancellationToken = default)
	{
		if (integration is IFolderViewProvider)
		{
			await _registry.UnregisterAll(integration.Id, cancellationToken);
		}
	}
}
