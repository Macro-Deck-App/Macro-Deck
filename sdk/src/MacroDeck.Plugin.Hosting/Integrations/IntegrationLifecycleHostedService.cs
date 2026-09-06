using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.FolderViews;
using MacroDeck.Sdk.Layouts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations;

/// <summary>
/// Initializes every registered integration once the plugin has an open connection, and shuts them
/// down in reverse on the way out.
///
/// <para>
/// <see cref="IIntegrationContext" /> is now real (issue #413 step 6): every one of its members is a
/// call back into the host, which means <c>InitializeAsync</c> cannot run before
/// <c>PluginConnectionHostedService</c> has a live session, or an integration that so much as reads a
/// config entry during startup would fail against a connection that does not exist yet. Initialization
/// is therefore gated on <see cref="PluginConnectionState.Connected" /> rather than run from
/// <see cref="StartAsync" /> directly - a change from the previous (throwing-context) behaviour, where
/// the ordering did not matter because nothing in the context could be called anyway.
/// </para>
///
/// <para>
/// A reconnect that is not a resume re-initializes: the host may have restarted, in which case
/// whatever state an integration cached from its first <c>InitializeAsync</c> (a config entry, a list
/// of variables) could be stale. A resume changes nothing - it is the same session continuing - so it
/// is intentionally a no-op here.
/// </para>
///
/// <para>
/// A failing integration is logged and skipped rather than taking the process down, matching the
/// in-process host's own isolation.
/// </para>
///
/// <para>
/// Also the subscriber for <see cref="HostStateCache.ConfigChanged" /> - see <see cref="OnConfigChanged" />.
/// </para>
/// </summary>
internal sealed class IntegrationLifecycleHostedService(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata,
	IIntegrationContext context,
	IDeviceProviderContext deviceContext,
	IHostInvoker hostInvoker,
	IServiceProvider services,
	ILayoutProviderContext layoutContext,
	IFolderViewProviderContext folderViewContext,
	IWidgetTypeProviderContext widgetTypeContext,
	PluginConnectionState connectionState,
	HostStateCache hostStateCache,
	ILogger logger) : IHostedService, IDisposable
{
	private readonly ILogger _logger = logger.ForContext<IntegrationLifecycleHostedService>();

	private readonly List<IPluginIntegration> _initialized = [];
	private readonly IReadOnlyList<IPluginIntegration> _integrations = [.. integrations];

	// Serializes StartAsync's subscription against a connect event that could otherwise race a
	// concurrent (re)initialization pass - PluginConnectionState.Connected is raised from the
	// connection's own async handshake path, not from this service.
	private readonly SemaphoreSlim _gate = new(1, 1);

	public Task StartAsync(CancellationToken cancellationToken)
	{
		connectionState.Connected += OnConnected;
		hostStateCache.ConfigChanged += OnConfigChanged;
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		connectionState.Connected -= OnConnected;
		hostStateCache.ConfigChanged -= OnConfigChanged;

		await _gate.WaitAsync(CancellationToken.None);
		try
		{
			await ShutdownAllAsync();
		}
		finally
		{
			_gate.Release();
		}
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e) => _ = HandleConnectedAsync(e.Resumed);

	/// <summary>
	/// The subscriber for <see cref="HostStateCache.ConfigChanged" /> (issue #413's config-change gap):
	/// the host pushes <c>host.state{config}</c> after a config flow completes for this plugin, and
	/// re-running every integration's shutdown/initialize cycle - the same as a non-resumed reconnect -
	/// is how a plugin picks up its new config without waiting for one. The host-side
	/// <c>RemotePluginIntegration.InitializeAsync</c> stays a no-op; this is the loop that actually
	/// re-reads the config now.
	/// </summary>
	private void OnConfigChanged() => _ = HandleConnectedAsync(resumed: false);

	private async Task HandleConnectedAsync(bool resumed)
	{
		if (resumed)
		{
			// The same session continuing - whatever was initialized on the first connect still applies.
			return;
		}

		await _gate.WaitAsync(CancellationToken.None);
		try
		{
			if (_initialized.Count > 0)
			{
				// A reconnect, not the first connect: the host may have restarted, so cached state from
				// the previous InitializeAsync can no longer be trusted.
				await ShutdownAllAsync();
			}

			foreach (var integration in _integrations)
			{
				try
				{
					await integration.InitializeAsync(context);

					// A widget type provider starts first among the provider hooks, before the layout
					// provider, the folder view provider and the device provider: a folder view or a
					// virtual profile may reference a widget type, so the types must exist before anything
					// that can name one.
					if (integration is IWidgetTypeProvider widgetTypeProvider)
					{
						await widgetTypeProvider.InitializeAsync(widgetTypeContext);
					}

					// A layout provider starts next, before any device provider: a provider that
					// implements both must have its layouts available by the time its device registers,
					// since a device's LayoutReference may point at one of them.
					if (integration is ILayoutProvider layoutProvider)
					{
						await layoutProvider.InitializeAsync(layoutContext);
					}

					// A folder view provider registers what a folder may point at, so like a layout it
					// starts before anything that could reference it.
					if (integration is IFolderViewProvider folderViewProvider)
					{
						await folderViewProvider.InitializeAsync(folderViewContext);
					}

					// A device provider starts after the integration it belongs to: discovery may well
					// depend on whatever InitializeAsync configured.
					if (integration is IDeviceProvider provider)
					{
						await provider.InitializeAsync(deviceContext);
					}

					// Likewise a push-capable variable catalog: attach it to its sink only after its own
					// InitializeAsync has run. Both flags gate the attach - a provider with no catalog has
					// nothing to push about, and one that does not push never uses the sink it is handed.
					// VariableSubscriptions is only registered
					// (MacroDeckServiceCollectionExtensions.AddCapabilitiesFor) when some integration in
					// this plugin actually implements IVariableProvider, so this resolves it through the
					// container rather than taking it as a required constructor dependency - a plugin with
					// no such provider must build with no trace of this capability at all.
					if (integration is IVariableProvider { SupportsCatalog: true, SupportsPush: true } catalog &&
						services.GetService<VariableSubscriptions>() is { } subscriptions)
					{
						var sink = new RemoteVariableSink(hostInvoker, subscriptions, logger);
						await catalog.OnAttachedAsync(sink);
					}

					_initialized.Add(integration);
					_logger.IntegrationInitialized(metadata.Id, integration.GetType().Name);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					_logger.IntegrationInitializationFailed(metadata.Id, integration.GetType().Name, exception);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task ShutdownAllAsync()
	{
		for (var index = _initialized.Count - 1; index >= 0; index--)
		{
			var integration = _initialized[index];

			try
			{
				if (integration is IDeviceProvider provider)
				{
					await provider.ShutdownAsync();
				}

				// None of ILayoutProvider, IFolderViewProvider or IWidgetTypeProvider has a ShutdownAsync of
				// its own - a provider releases whatever InitializeAsync acquired from the integration's
				// own ShutdownAsync below.
				await integration.ShutdownAsync();
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				// Shutdown is best effort by contract: one integration refusing to let go must not
				// stop the others from draining.
				_logger.IntegrationShutdownFailed(metadata.Id, integration.GetType().Name, exception);
			}
		}

		_initialized.Clear();
	}

	public void Dispose() => _gate.Dispose();
}
