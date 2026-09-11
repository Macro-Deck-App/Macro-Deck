using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Capabilities.FolderViewProvider;
using MacroDeck.Plugin.Protocol.Capabilities.LayoutProvider;
using MacroDeck.Plugin.Protocol.Capabilities.Localization;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using MacroDeck.Plugin.Protocol.Capabilities.WidgetTypeProvider;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed class RemotePluginIntegrationRegistrar : IRemotePluginIntegrationRegistrar, IDisposable
{
	private static readonly HashSet<string> _perItemKinds = new(StringComparer.Ordinal)
	{
		CapabilityKinds.Actions, CapabilityKinds.Variables
	};

	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IRemotePluginSnapshotStore _snapshotStore;
	private readonly RemotePluginSnapshotRefresher _refresher;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IRemotePluginConnectionState _connectionState;
	private readonly IPluginAssetCache _assetCache;
	private readonly IPluginAssetReceiver _assetReceiver;
	private readonly IPluginInstallationCatalog _installationCatalog;
	private readonly IPluginManifestReader _manifestReader;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly ILocalizationCatalogRegistry _localizationCatalogs;
	private readonly IPluginDeviceRegistry _deviceRegistry;
	private readonly ILayoutRegistry _layoutRegistry;
	private readonly IFolderViewRegistry _folderViewRegistry;
	private readonly IWidgetTypeRegistry _widgetTypeRegistry;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly RemoteVariableSubscriptions? _variableSubscriptions;

	private readonly ConcurrentDictionary<(string PluginId, string ContentHash), TaskCompletionSource<byte[]>>
		_iconWaiters
			= new();

	public RemotePluginIntegrationRegistrar(
		IPluginSessionRegistry sessionRegistry,
		IIntegrationRegistry integrationRegistry,
		IRemotePluginSnapshotStore snapshotStore,
		RemotePluginSnapshotRefresher refresher,
		IPluginCapabilityInvoker invoker,
		IRemotePluginConnectionState connectionState,
		IPluginAssetCache assetCache,
		IPluginAssetReceiver assetReceiver,
		IPluginInstallationCatalog installationCatalog,
		IPluginManifestReader manifestReader,
		IUserNotificationStore userNotificationStore,
		IServiceScopeFactory serviceScopeFactory,
		ILocalizationCatalogRegistry localizationCatalogs,
		IPluginDeviceRegistry deviceRegistry,
		ILayoutRegistry layoutRegistry,
		IFolderViewRegistry folderViewRegistry,
		IWidgetTypeRegistry widgetTypeRegistry,
		TimeProvider timeProvider,
		ILogger logger,
		RemoteVariableSubscriptions? variableSubscriptions = null)
	{
		_sessionRegistry = sessionRegistry;
		_integrationRegistry = integrationRegistry;
		_snapshotStore = snapshotStore;
		_refresher = refresher;
		_invoker = invoker;
		_connectionState = connectionState;
		_assetCache = assetCache;
		_assetReceiver = assetReceiver;
		_installationCatalog = installationCatalog;
		_manifestReader = manifestReader;
		_userNotificationStore = userNotificationStore;
		_serviceScopeFactory = serviceScopeFactory;
		_localizationCatalogs = localizationCatalogs;
		_deviceRegistry = deviceRegistry;
		_layoutRegistry = layoutRegistry;
		_folderViewRegistry = folderViewRegistry;
		_widgetTypeRegistry = widgetTypeRegistry;
		_timeProvider = timeProvider;
		_logger = logger;
		_variableSubscriptions = variableSubscriptions;

		_sessionRegistry.SessionEnded += OnSessionEnded;
		_assetReceiver.AssetCommitted += OnAssetCommitted;
	}

	public async Task<bool> RegisterAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		var capabilities = _sessionRegistry.GetCapabilities(pluginId);
		if (capabilities is null)
		{
			_logger.Warning("Cannot register plugin '{PluginId}': it has no session", pluginId);
			return false;
		}

		var acceptedKinds = capabilities.Capabilities
			.Where(pair => pair.Value.Accepted)
			.Select(pair => pair.Key)
			.ToList();

		var conflicts = Validate(pluginId, capabilities.DeclaredCapabilities, acceptedKinds);
		if (conflicts.Count > 0)
		{
			await RejectAsync(pluginId,
				DisplayNameOf(pluginId),
				IntegrationRegistrationResult.Invalid(conflicts),
				cancellationToken).ConfigureAwait(false);
			return false;
		}

		var hadPriorSnapshot = _snapshotStore.Has(pluginId);
		var refreshResult = await _refresher.RefreshAsync(pluginId, acceptedKinds, cancellationToken)
			.ConfigureAwait(false);

		if (!refreshResult.AllSucceeded && !hadPriorSnapshot)
		{
			// No fallback to retain - a transient failure here must not silently register an adapter
			// whose declared catalogue the host never actually saw. See section C's describe-failure rule.
			var reason = $"Failed to describe: {string.Join(", ", refreshResult.FailedKinds)}.";
			await RejectAsync(pluginId,
					DisplayNameOf(pluginId),
					new IntegrationRegistrationResult(false,
						IntegrationRegistrationFailure.InvalidCapabilityIds,
						[new CapabilityIdConflict(pluginId, "Capability", string.Empty, reason)]),
					cancellationToken)
				.ConfigureAwait(false);
			return false;
		}

		var displayName = DisplayNameOf(pluginId);
		var version = ResolveVersion(pluginId);
		var snapshot = await ResolveIconBytesAsync(refreshResult.Snapshot, cancellationToken).ConfigureAwait(false);

		var adapter = RemotePluginIntegrationFactory.Create(pluginId,
			displayName,
			version,
			snapshot,
			_invoker,
			_connectionState,
			_assetCache,
			hasIcon: snapshot.IconBytes.Length > 0,
			hasConfigFlow: acceptedKinds.Contains(CapabilityKinds.ConfigFlow),
			hasDynamicEventOptions: refreshResult.Snapshot.HasDynamicEventOptions);
		adapter.VariableSubscriptions = _variableSubscriptions;

		var registration = await _integrationRegistry.RegisterAsync(adapter,
			IntegrationOrigin.Plugin,
			IntegrationMetadata.Default).ConfigureAwait(false);

		if (!registration.Registered)
		{
			await RejectAsync(pluginId, displayName, registration, cancellationToken).ConfigureAwait(false);
			return false;
		}

		await PublishStateChangedAsync(pluginId, cancellationToken).ConfigureAwait(false);

		if (acceptedKinds.Contains(CapabilityKinds.Localization))
		{
			await RegisterLocalizationCatalogAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}

		// Widget types before layouts, folder views and devices: a folder view or a virtual profile may
		// name one of the plugin's own widget types, and it must already resolve by the time those
		// register.
		if (acceptedKinds.Contains(CapabilityKinds.WidgetTypeProvider))
		{
			await RegisterProviderWidgetTypesAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}

		// Layouts before devices: a device that references one of the plugin's own layouts must be able
		// to resolve it on this very reconnect, not only after some later re-registration.
		if (acceptedKinds.Contains(CapabilityKinds.LayoutProvider))
		{
			await RegisterProviderLayoutsAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}

		if (acceptedKinds.Contains(CapabilityKinds.DeviceProvider))
		{
			await RegisterProviderDevicesAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}

		if (acceptedKinds.Contains(CapabilityKinds.FolderViewProvider))
		{
			await RegisterProviderFolderViewsAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public async Task UnregisterAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		var scope = LocalizationScope.ForPlugin(pluginId);
		var hadLocalizationCatalog = _localizationCatalogs.Unregister(scope);

		// WidgetTypeProviderHost.StopAsync, FolderViewProviderHost.StopAsync and LayoutProviderHost.StopAsync
		// only ever run from IntegrationLifecycle, which this path - uninstall among its callers - does not
		// go through. Without withdrawing them here, a plugin's widget types, folder views and layouts would
		// all survive its uninstall until the next host restart.
		await _widgetTypeRegistry.UnregisterAll(pluginId, cancellationToken).ConfigureAwait(false);
		await _folderViewRegistry.UnregisterAll(pluginId, cancellationToken).ConfigureAwait(false);
		await _layoutRegistry.UnregisterAll(pluginId, cancellationToken).ConfigureAwait(false);

		await _integrationRegistry.UnregisterAsync(pluginId).ConfigureAwait(false);

		if (hadLocalizationCatalog)
		{
			await PublishLocalizationCatalogChangedAsync(scope, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task RefreshLocalizationCatalogsAsync(CancellationToken cancellationToken = default)
	{
		foreach (var scope in _localizationCatalogs.Scopes)
		{
			if (LocalizationScope.PluginIdOf(scope) is { } pluginId)
			{
				await RegisterLocalizationCatalogAsync(pluginId, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	// Reads back what the provider already has, so a plugin that reconnects mid-session does not have to
	// wait for its own discovery to run again before its hardware reappears. Best-effort for the same
	// reason the localization catalog below is: a failure here must not unregister the plugin, and the
	// provider's own register calls remain the primary path.
	// Same best-effort treatment as RegisterProviderDevicesAsync below: a failure here must not
	// unregister the plugin, and the provider's own register/unregister callbacks remain the primary
	// path (see PluginCallbackRouter's HostApis.Layouts branch).
	private async Task RegisterProviderLayoutsAsync(string pluginId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.LayoutProvider,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.LayoutProvider.Layouts
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<LayoutProviderLayoutsResult>(PluginProtocolJson.Options);
			if (result is null)
			{
				return;
			}

			foreach (var layout in result.Layouts)
			{
				try
				{
					await _layoutRegistry.Register(pluginId,
							LayoutDescriptorMapper.ToDescriptor(layout),
							cancellationToken)
						.ConfigureAwait(false);
				}
				catch (ArgumentException exception)
				{
					_logger.Warning(exception,
						"Rejected a layout declared by plugin '{PluginId}' after it connected",
						pluginId);
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Could not read the layouts of plugin '{PluginId}' after it connected",
				pluginId);
		}
	}

	// Same best-effort treatment as RegisterProviderLayoutsAsync: a failure here must not unregister the
	// plugin, and the provider's own register/unregister callbacks remain the primary path (see
	// PluginCallbackRouter's HostApis.FolderViews branch). A folder that selected a view this read did not
	// recover simply shows the placeholder until the provider registers it again - it is never reset.
	private async Task RegisterProviderFolderViewsAsync(string pluginId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.FolderViewProvider,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.FolderViewProvider.FolderViews
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<FolderViewProviderFolderViewsResult>(PluginProtocolJson.Options);
			if (result is null)
			{
				return;
			}

			foreach (var folderView in result.FolderViews)
			{
				try
				{
					await _folderViewRegistry.Register(pluginId,
							FolderViewDescriptorMapper.ToDescriptor(folderView),
							cancellationToken)
						.ConfigureAwait(false);
				}
				catch (ArgumentException exception)
				{
					_logger.Warning(exception,
						"Rejected a folder view declared by plugin '{PluginId}' after it connected",
						pluginId);
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Could not read the folder views of plugin '{PluginId}' after it connected",
				pluginId);
		}
	}

	// Same best-effort treatment as RegisterProviderLayoutsAsync: a failure here must not unregister the
	// plugin, and the provider's own register/unregister callbacks remain the primary path (see
	// PluginCallbackRouter's HostApis.WidgetTypes branch). A widget already placed with one of the types
	// this read did not recover simply has nothing to draw it until the provider registers it again - it
	// is never reset.
	private async Task RegisterProviderWidgetTypesAsync(string pluginId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.WidgetTypeProvider,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.WidgetTypeProvider.WidgetTypes
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<WidgetTypeProviderWidgetTypesResult>(PluginProtocolJson.Options);
			if (result is null)
			{
				return;
			}

			foreach (var widgetType in result.WidgetTypes)
			{
				try
				{
					await _widgetTypeRegistry.Register(pluginId,
							WidgetTypeDescriptorMapper.ToDescriptor(widgetType),
							cancellationToken)
						.ConfigureAwait(false);
				}
				catch (ArgumentException exception)
				{
					_logger.Warning(exception,
						"Rejected a widget type declared by plugin '{PluginId}' after it connected",
						pluginId);
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Could not read the widget types of plugin '{PluginId}' after it connected",
				pluginId);
		}
	}

	private async Task RegisterProviderDevicesAsync(string pluginId, CancellationToken cancellationToken)
	{
		try
		{
			var data = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.DeviceProvider,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.DeviceProvider.Devices
					},
					cancellationToken)
				.ConfigureAwait(false);

			var result = data?.Deserialize<DeviceProviderDevicesResult>(PluginProtocolJson.Options);
			if (result is null)
			{
				return;
			}

			foreach (var device in result.Devices)
			{
				await _deviceRegistry
					.RegisterAsync(pluginId, DeviceDescriptorMapper.ToDescriptor(device), cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Could not read the devices of plugin '{PluginId}' after it connected",
				pluginId);
		}
	}

	// Best-effort and never throws: a plugin's localization catalog is a secondary capability layered on
	// top of RegisterAsync's main adapter registration, so a describe/catalog failure or a limit
	// violation here must leave the plugin itself registered and its other capabilities untouched - it
	// is only logged and the scope is left as it was (absent on first registration, unchanged on a
	// refresh that failed).
	private async Task RegisterLocalizationCatalogAsync(string pluginId, CancellationToken cancellationToken)
	{
		var scope = LocalizationScope.ForPlugin(pluginId);

		try
		{
			var describeData = await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Localization,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Localization.Describe
					},
					cancellationToken)
				.ConfigureAwait(false);

			var describe = describeData?.Deserialize<LocalizationDescribeResult>(PluginProtocolJson.Options);
			if (describe is null)
			{
				return;
			}

			if (!string.Equals(describe.Scope, scope, StringComparison.Ordinal))
			{
				_logger.Warning("Rejected the localization catalog declared by plugin '{PluginId}': it claimed scope " +
					"'{ClaimedScope}' instead of its own '{OwnScope}'",
					pluginId,
					describe.Scope,
					scope);
				return;
			}

			if (describe.Cultures.Count == 0 || describe.Cultures.Count > ProtocolLimits.MaxLocalizationCultures)
			{
				_logger.Warning(
					"Rejected the localization catalog declared by plugin '{PluginId}': it declared {Count} " +
					"cultures, the limit is {Limit}",
					pluginId,
					describe.Cultures.Count,
					ProtocolLimits.MaxLocalizationCultures);
				return;
			}

			var activeCulture = await ResolveActiveCultureAsync(cancellationToken).ConfigureAwait(false);
			var declaredCultures = new HashSet<string>(describe.Cultures, StringComparer.OrdinalIgnoreCase);
			var culturesToFetch = LocalizationCultureChain.For(activeCulture, describe.DefaultCulture)
				.Where(declaredCultures.Contains)
				.ToList();

			var templatesByCulture =
				new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

			foreach (var culture in culturesToFetch)
			{
				var catalogData = await _invoker.InvokeAsync(pluginId,
						new CapabilityInvokeRequest
						{
							Kind = CapabilityKinds.Localization,
							LocalId = ProviderCapabilityId.LocalId,
							Operation = CapabilityOperations.Localization.Catalog,
							Arguments = new LocalizationCatalogArguments { Culture = culture }
						},
						cancellationToken)
					.ConfigureAwait(false);

				var catalogResult = catalogData?.Deserialize<LocalizationCatalogResult>(PluginProtocolJson.Options);
				if (catalogResult is null)
				{
					continue;
				}

				if (!TryValidateCatalogEntries(catalogResult, out var invalidReason))
				{
					_logger.Warning("Rejected the localization catalog declared by plugin '{PluginId}' for culture " +
						"'{Culture}': {Reason}",
						pluginId,
						culture,
						invalidReason);
					return;
				}

				templatesByCulture[catalogResult.Culture] = catalogResult.Entries;
			}

			if (templatesByCulture.Count == 0)
			{
				return;
			}

			var keys = templatesByCulture.Values
				.SelectMany(templates => templates.Keys)
				.Distinct(StringComparer.Ordinal)
				.ToList();

			_localizationCatalogs.Register(new LocalizationCatalog(scope, describe.DefaultCulture, templatesByCulture));

			await PublishLocalizationCatalogChangedAsync(scope, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is RemoteCapabilityException or OperationCanceledException)
		{
			_logger.Warning(exception, "Failed to fetch a localization catalog from plugin '{PluginId}'", pluginId);
		}
	}

	private static bool TryValidateCatalogEntries(LocalizationCatalogResult catalog, out string reason)
	{
		if (catalog.Entries.Count > ProtocolLimits.MaxLocalizationEntries)
		{
			reason =
				$"{catalog.Entries.Count} entries exceeds the limit of {ProtocolLimits.MaxLocalizationEntries}";
			return false;
		}

		foreach (var entry in catalog.Entries)
		{
			if (entry.Key.Length > ProtocolLimits.MaxLocalizationKeyLength)
			{
				reason = $"key '{entry.Key}' exceeds {ProtocolLimits.MaxLocalizationKeyLength} characters";
				return false;
			}

			if (entry.Value.Length > ProtocolLimits.MaxLocalizationValueLength)
			{
				reason = $"the value for key '{entry.Key}' exceeds {ProtocolLimits.MaxLocalizationValueLength} " +
					"characters";
				return false;
			}
		}

		reason = string.Empty;
		return true;
	}

	private async Task<string> ResolveActiveCultureAsync(CancellationToken cancellationToken)
	{
		await using var scope = _serviceScopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		var settings = await preferences.GetLocalization().ConfigureAwait(false);
		return settings.Culture;
	}

	private async Task PublishLocalizationCatalogChangedAsync(string scope, CancellationToken cancellationToken)
	{
		await using var serviceScope = _serviceScopeFactory.CreateAsyncScope();
		var mediator = serviceScope.ServiceProvider.GetRequiredService<IMediator>();
		await mediator.Publish(new LocalizationCatalogChangedNotification(scope), cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task ApplyRefreshedSnapshotAsync(
		string pluginId,
		RemotePluginCapabilitySnapshot snapshot,
		CancellationToken cancellationToken = default)
	{
		var current = _integrationRegistry.Integrations
			.FirstOrDefault(integration => string.Equals(integration.Id, pluginId, StringComparison.Ordinal));

		if (current is null)
		{
			return;
		}

		var adapter = RemotePluginIntegrationFactory.Create(pluginId,
			current.Name,
			current.Version,
			snapshot,
			_invoker,
			_connectionState,
			_assetCache,
			hasIcon: current is IIntegrationIconProvider,
			hasConfigFlow: current is IConfigFlowProvider,
			hasDynamicEventOptions: current is IDynamicEventOptionsProvider);
		adapter.VariableSubscriptions = _variableSubscriptions;

		await _integrationRegistry.UnregisterAsync(pluginId).ConfigureAwait(false);

		var registration = await _integrationRegistry.RegisterAsync(adapter,
			IntegrationOrigin.Plugin,
			IntegrationMetadata.Default).ConfigureAwait(false);

		if (registration.Registered)
		{
			await PublishStateChangedAsync(pluginId, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task<RemotePluginCapabilitySnapshot> ResolveIconBytesAsync(
		RemotePluginCapabilitySnapshot snapshot,
		CancellationToken cancellationToken)
	{
		if (!snapshot.HasIcon)
		{
			return snapshot;
		}

		if (string.Equals(snapshot.IconContentHash, snapshot.IconBytesContentHash, StringComparison.Ordinal) &&
			snapshot.IconBytes.Length > 0)
		{
			return snapshot;
		}

		var bytes = await WaitForIconAsync(snapshot.PluginId, snapshot.IconContentHash, cancellationToken)
			.ConfigureAwait(false);

		if (bytes is null)
		{
			// The wait gave up - IconBytes/IconBytesContentHash, if set at all, are known stale (the
			// mismatch check above already ruled out them matching IconContentHash), so they are cleared
			// rather than left in place. The caller reads IconBytes.Length alone to decide hasIcon; a
			// stale non-empty array from an icon this describe superseded must not read as "arrived".
			return snapshot with { IconBytes = [], IconBytesContentHash = string.Empty };
		}

		var updated = snapshot with { IconBytes = bytes, IconBytesContentHash = snapshot.IconContentHash };
		await _snapshotStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
		return updated;
	}

	private async Task<byte[]?> WaitForIconAsync(string pluginId,
		string contentHash,
		CancellationToken cancellationToken)
	{
		if (_assetCache.TryRead(contentHash, out var cached, out _))
		{
			return cached;
		}

		var key = (pluginId, contentHash);
		var completion = _iconWaiters.GetOrAdd(key,
			static _ => new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));

		try
		{
			var timeoutTask = Task.Delay(ProtocolTimeouts.AssetUpload, _timeProvider, cancellationToken);
			var winner = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
			return winner == completion.Task ? await completion.Task.ConfigureAwait(false) : null;
		}
		finally
		{
			_iconWaiters.TryRemove(key, out _);
		}
	}

	private void OnAssetCommitted(object? sender, AssetCommittedEventArgs e)
	{
		if (!string.Equals(e.Kind, AssetKinds.Icon, StringComparison.Ordinal))
		{
			return;
		}

		if (_iconWaiters.TryGetValue((e.PluginId, e.ContentHash), out var waiter))
		{
			waiter.TrySetResult(e.Bytes);
		}

		_ = HandleIconCommittedAsync(e);
	}

	private async Task HandleIconCommittedAsync(AssetCommittedEventArgs e)
	{
		try
		{
			var snapshot = _snapshotStore.GetSnapshot(e.PluginId) with
			{
				IconBytes = e.Bytes, IconMimeType = e.MimeType, IconBytesContentHash = e.ContentHash
			};
			await _snapshotStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

			var current = _integrationRegistry.Integrations
				.FirstOrDefault(integration => string.Equals(integration.Id, e.PluginId, StringComparison.Ordinal));


			if (current is not null)
			{
				await SwapIconOnlyAsync(e.PluginId, current, snapshot).ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to apply a committed icon asset for plugin '{PluginId}'", e.PluginId);
		}
	}

	private async Task SwapIconOnlyAsync(string pluginId, IIntegration current, RemotePluginCapabilitySnapshot snapshot)
	{
		var adapter = RemotePluginIntegrationFactory.Create(pluginId,
			current.Name,
			current.Version,
			snapshot,
			_invoker,
			_connectionState,
			_assetCache,
			hasIcon: true,
			hasConfigFlow: current is IConfigFlowProvider,
			hasDynamicEventOptions: current is IDynamicEventOptionsProvider);
		adapter.VariableSubscriptions = _variableSubscriptions;

		await _integrationRegistry.UnregisterAsync(pluginId).ConfigureAwait(false);

		var registration = await _integrationRegistry.RegisterAsync(adapter,
			IntegrationOrigin.Plugin,
			IntegrationMetadata.Default).ConfigureAwait(false);

		if (registration.Registered)
		{
			await PublishStateChangedAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
		}
	}

	public async Task RegisterInstalledButStoppedAsync(CancellationToken cancellationToken = default)
	{
		var connectedPluginIds = new HashSet<string>(_sessionRegistry.Snapshot().Select(session => session.PluginId),
			StringComparer.Ordinal);

		foreach (var installed in InstalledPlugins())
		{
			if (connectedPluginIds.Contains(installed.PluginId))
			{
				continue;
			}

			await RegisterDetachedAsync(installed).ConfigureAwait(false);
		}
	}

	public async Task RegisterInstalledDetachedAsync(string pluginId,
		CancellationToken cancellationToken = default)
	{
		if (_sessionRegistry.Snapshot().Any(session =>
			string.Equals(session.PluginId, pluginId, StringComparison.Ordinal)))
		{
			return;
		}

		var installed = InstalledPlugins()
			.FirstOrDefault(candidate => string.Equals(candidate.PluginId, pluginId, StringComparison.Ordinal));

		if (installed is null)
		{
			return;
		}

		await RegisterDetachedAsync(installed).ConfigureAwait(false);
	}

	public async Task UnregisterVanishedInstallationsAsync(CancellationToken cancellationToken = default)
	{
		// Discover() lists one entry per plugin directory whether or not it holds versions, so a
		// half-removed install still counts as present and keeps its card - it is still uninstallable
		// from the page, which is how a user gets rid of it. Only an id whose directory is gone entirely
		// has vanished.
		var present = _installationCatalog.Discover()
			.Select(plugin => plugin.PluginId)
			.ToHashSet(StringComparer.Ordinal);

		foreach (var integration in _integrationRegistry.Integrations.ToList())
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (_integrationRegistry.GetOrigin(integration.Id) != IntegrationOrigin.Plugin ||
				present.Contains(integration.Id))
			{
				continue;
			}

			// Read per candidate rather than once up front: this runs on a timer against a registry that
			// a connecting plugin writes to, and a session that arrives mid-sweep must keep the adapter
			// it just registered. A plugin with no installation and a session is a developer build.
			if (HasSession(integration.Id))
			{
				continue;
			}

			await UnregisterAsync(integration.Id, cancellationToken).ConfigureAwait(false);
			_logger.Warning("Unregistered integration '{PluginId}': its installation is no longer on disk",
				integration.Id);
		}
	}

	private bool HasSession(string pluginId)
		=> _sessionRegistry.Snapshot()
			.Any(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));

	private IEnumerable<InstalledPlugin> InstalledPlugins()
		=> _installationCatalog.Discover().Where(plugin => plugin.Versions.Count > 0);

	private async Task RegisterDetachedAsync(InstalledPlugin installed)
	{
		var snapshot = _snapshotStore.GetSnapshot(installed.PluginId);
		var displayName = PluginDisplayNameResolver.Resolve(session: null,
			ManifestNameOf(installed),
			installed.PluginId);
		var version = PluginVersionResolver.Resolve(installed.ActiveVersion?.Version, declaredSessionVersion: null);

		var adapter = RemotePluginIntegrationFactory.Create(installed.PluginId,
			displayName,
			version,
			snapshot,
			_invoker,
			_connectionState,
			_assetCache,
			hasIcon: string.Equals(snapshot.IconContentHash, snapshot.IconBytesContentHash, StringComparison.Ordinal) &&
			snapshot.IconBytes.Length > 0,
			hasConfigFlow: snapshot.AcceptedKinds.Contains(CapabilityKinds.ConfigFlow),
			hasDynamicEventOptions: snapshot.HasDynamicEventOptions);
		adapter.VariableSubscriptions = _variableSubscriptions;

		var registration = await _integrationRegistry.RegisterAsync(adapter,
			IntegrationOrigin.Plugin,
			IntegrationMetadata.Default).ConfigureAwait(false);

		if (!registration.Registered)
		{
			_logger.Warning("Could not register a detached adapter for installed plugin '{PluginId}': {Reason}",
				installed.PluginId,
				registration.Describe());
		}
	}

	public void Dispose()
	{
		_sessionRegistry.SessionEnded -= OnSessionEnded;
		_assetReceiver.AssetCommitted -= OnAssetCommitted;
	}

	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		// Detached (resumable) is deliberately not handled here - the adapter stays registered and
		// degrades live via IRemotePluginConnectionState. Only Pruned (the resume window elapsed, or an
		// explicit terminate) is a truly gone session.
		if (e.Reason != PluginSessionEndReason.Pruned)
		{
			return;
		}

		// PluginSessionRegistry.Create raises this same event for the record a new session replaces, and
		// by the time it does, the new session is already registered under this plugin id. Keying on
		// PluginId alone would let the replaced record's own prune - arriving after the new session took
		// over - unregister the *new* session's adapter, racing its RegisterAsync with nothing left to
		// re-add it. Only unregister when the plugin id genuinely has no live session, or still points at
		// the very session that just ended.
		var current = _sessionRegistry.Snapshot()
			.FirstOrDefault(session => string.Equals(session.PluginId, e.PluginId, StringComparison.Ordinal));
		if (current is not null && !string.Equals(current.SessionId, e.SessionId, StringComparison.Ordinal))
		{
			return;
		}

		_ = UnregisterPrunedAsync(e.PluginId);
	}

	private async Task UnregisterPrunedAsync(string pluginId)
	{
		try
		{
			await UnregisterAsync(pluginId).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Failed to unregister plugin '{PluginId}' after its session was pruned",
				pluginId);
		}
	}

	private static List<CapabilityIdConflict> Validate(
		string pluginId,
		IReadOnlyList<DeclaredCapability> declaredCapabilities,
		IReadOnlyCollection<string> acceptedKinds)
	{
		var conflicts = new List<CapabilityIdConflict>();

		var ownerConflict = DeclaredIdValidator.ValidateOwner(pluginId, OwnerIdKind.Package, "Plugin");
		if (ownerConflict is not null)
		{
			conflicts.Add(ownerConflict);
			return conflicts;
		}

		var byKind = declaredCapabilities
			.Where(capability => acceptedKinds.Contains(capability.Kind))
			.GroupBy(capability => capability.Kind, StringComparer.Ordinal);

		foreach (var group in byKind)
		{
			var localIds = group.Select(capability => capability.LocalId).ToList();

			if (_perItemKinds.Contains(group.Key))
			{
				DeclaredIdValidator.Validate(conflicts, pluginId, CapabilityTypeName(group.Key), localIds);
				continue;
			}

			if (localIds.Count != 1)
			{
				conflicts.Add(new CapabilityIdConflict(pluginId,
					CapabilityTypeName(group.Key),
					string.Join(", ", localIds),
					$"The '{group.Key}' capability must declare exactly one local id, found {localIds.Count}."));
				continue;
			}

			DeclaredIdValidator.Validate(conflicts, pluginId, CapabilityTypeName(group.Key), localIds);
		}

		return conflicts;
	}

	private static string CapabilityTypeName(string kind)
		=> kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind[1..];

	private async Task RejectAsync(
		string pluginId,
		string displayName,
		IntegrationRegistrationResult registration,
		CancellationToken cancellationToken)
	{
		IntegrationRegistrationRejectionNotifier.Raise(_userNotificationStore, pluginId, displayName, registration);

		var error = new ProtocolError
		{
			Code = ProtocolErrorCodes.InvalidPayload,
			Message = registration.Describe() is { Length: > 0 } describe
				? describe
				: "The host rejected this plugin's declared capabilities.",
			Retryable = false
		};

		await _sessionRegistry.SendToPlugin(pluginId,
				new ProtocolEnvelope
				{
					Type = MessageTypes.ProtocolError,
					Id = Guid.CreateVersion7().ToString(),
					Error = error,
					Payload = JsonSerializer.SerializeToElement(error, PluginProtocolJson.Options)
				},
				cancellationToken)
			.ConfigureAwait(false);

		await _sessionRegistry.TerminateForPlugin(pluginId,
			ProtocolCloseCodes.RegistrationRejected,
			"Registration rejected.").ConfigureAwait(false);
	}

	private async Task PublishStateChangedAsync(string pluginId, CancellationToken cancellationToken)
	{
		await using var scope = _serviceScopeFactory.CreateAsyncScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		await mediator.Publish(new IntegrationStateChangedNotification(pluginId), cancellationToken)
			.ConfigureAwait(false);
	}

	private string DisplayNameOf(string pluginId)
	{
		var session = SessionSnapshotFor(pluginId);

		if (session is not null)
		{
			return PluginDisplayNameResolver.Resolve(session, manifestName: null, pluginId);
		}

		var installed = _installationCatalog.Discover()
			.FirstOrDefault(candidate => string.Equals(candidate.PluginId, pluginId, StringComparison.Ordinal));

		return PluginDisplayNameResolver.Resolve(session: null,
			manifestName: installed is not null ? ManifestNameOf(installed) : null,
			pluginId);
	}

	private string ResolveVersion(string pluginId)
	{
		var installedVersion = _installationCatalog.TryResolveActive(pluginId, out var version)
			? version?.Version
			: null;

		return PluginVersionResolver.Resolve(installedVersion, SessionSnapshotFor(pluginId)?.DeclaredVersion);
	}

	private PluginSessionSnapshot? SessionSnapshotFor(string pluginId)
		=> _sessionRegistry.Snapshot()
			.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal));

	private string? ManifestNameOf(InstalledPlugin installed)
	{
		if (installed.ActiveVersion is not { } active)
		{
			return null;
		}

		var result = _manifestReader.Read(active.ManifestPath, installed.PluginId, active.Version);
		return result.Success ? result.Manifest!.Name : null;
	}
}
