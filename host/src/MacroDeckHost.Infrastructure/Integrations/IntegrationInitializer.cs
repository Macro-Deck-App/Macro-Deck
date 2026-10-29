using System.Collections.Concurrent;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Infrastructure.Variables;
using MacroDeckHost.Infrastructure.Widgets;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Widgets;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Companion;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Http;
using MacroDeck.Sdk.Locale;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Integrations;

public enum IntegrationInitializationOutcome
{
	Initialized,
	TimedOut,
	Failed,
	Aborted
}

public sealed class IntegrationInitializer
{
	private static readonly TimeSpan _initializeTimeout = TimeSpan.FromSeconds(30);

	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly IDeckNavigator _deckNavigator;
	private readonly IScriptApi _scriptApi;
	private readonly IWidgetApi _widgetApi;
	private readonly IWidgetIconInvalidator _widgetIconInvalidator;
	private readonly IUserVariableApi _userVariableApi;
	private readonly IEventBus _eventBus;
	private readonly IEventBindingTracker _bindingTracker;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly IVariableBindingStore _bindingStore;
	private readonly IVariableRefreshSignal _refreshSignal;
	private readonly IIntegrationHostIssueStore _hostIssueStore;
	private readonly LayoutProviderHost _layoutProviders;
	private readonly FolderViewProviderHost _folderViewProviders;
	private readonly ScreenSaverProviderHost _screenSaverProviders;
	private readonly VideoStreamProviderHost _videoStreamProviders;
	private readonly WidgetTypeProviderHost _widgetTypeProviders;
	private readonly DeviceProviderHost _deviceProviders;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly IVariablePollingInvalidationSignal? _pollingInvalidation;
	private readonly CalendarHostServices? _calendarServices;
	private readonly IMusicPlayerAlbumColor? _albumColor;
	private readonly IMusicPlayerRegistry? _musicPlayerRegistry;
	private readonly IMusicPlayerStateNotifier? _musicPlayerNotifier;
	private readonly INotificationManager? _notificationManager;
	private readonly IUiResourceStore? _uiResourceStore;
	private readonly IMusicPlayerArtworkService? _musicPlayerArtwork;
	private readonly IWidgetDataReader? _widgetData;
	private readonly OwnedVariableHistory? _variableHistory;
	private readonly EmailHostServices? _emailServices;
	private readonly ConcurrentDictionary<string, BuiltInUiResources> _uiResources = new(StringComparer.Ordinal);
	private readonly IBatteryRegistry? _batteries;
	private readonly BatteryProviderHost? _batteryProviders;
	private readonly InProcessAndroidDeviceManager? _androidDevices;
	private readonly IntegrationWeatherProviderApi? _weatherProvider;
	private readonly IntegrationStorageProvider? _storage;
	private readonly ILocaleApi? _locale;
	private readonly IHostHttp? _http;
	private readonly ICompanionDeviceApi? _companionDevices;
	private readonly IDesktopNotifier? _desktopNotifications;

	private readonly ConcurrentDictionary<string, byte> _attempted = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, IntegrationEventPublisher> _eventPublishers
		= new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, InProcessMessageChannel> _messageChannels
		= new(StringComparer.Ordinal);

	private readonly IMessageBroker _messageBroker;
	private readonly PluginColorWatches _colorWatches;
	private readonly ConcurrentDictionary<string, InProcessColorApi> _colorApis = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, InProcessBatteryApi> _batteryApis = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, InProcessMusicPlayerApi> _musicPlayerApis
		= new(StringComparer.Ordinal);

	public IntegrationInitializer(
		IServiceScopeFactory serviceScopeFactory,
		IDeckNavigator deckNavigator,
		IScriptApi scriptApi,
		IWidgetApi widgetApi,
		IWidgetIconInvalidator widgetIconInvalidator,
		IUserVariableApi userVariableApi,
		IEventBus eventBus,
		IEventBindingTracker bindingTracker,
		IUserNotificationStore userNotificationStore,
		IVariableBindingStore bindingStore,
		IVariableRefreshSignal refreshSignal,
		IIntegrationHostIssueStore hostIssueStore,
		LayoutProviderHost layoutProviders,
		FolderViewProviderHost folderViewProviders,
		WidgetTypeProviderHost widgetTypeProviders,
		ScreenSaverProviderHost screenSaverProviders,
		VideoStreamProviderHost videoStreamProviders,
		DeviceProviderHost deviceProviders,
		TimeProvider timeProvider,
		ILogger logger,
		IMessageBroker messageBroker,
		PluginColorWatches colorWatches,
		IVariablePollingInvalidationSignal? pollingInvalidation = null,
		CalendarHostServices? calendarServices = null,
		IMusicPlayerAlbumColor? albumColor = null,
		INotificationManager? notificationManager = null,
		IUiResourceStore? uiResourceStore = null,
		IMusicPlayerArtworkService? musicPlayerArtwork = null,
		IWidgetDataReader? widgetData = null,
		OwnedVariableHistory? variableHistory = null,
		IBatteryRegistry? batteries = null,
		BatteryProviderHost? batteryProviders = null,
		InProcessAndroidDeviceManager? androidDevices = null,
		IWeatherBroadcastTrigger? weatherTrigger = null,
		IntegrationStorageProvider? storage = null,
		ILocaleApi? locale = null,
		IHostHttp? http = null,
		ICompanionDeviceApi? companionDevices = null,
		IMusicPlayerRegistry? musicPlayerRegistry = null,
		IMusicPlayerStateNotifier? musicPlayerNotifier = null,
		EmailHostServices? emailServices = null,
		IDesktopNotifier? desktopNotifications = null)
	{
		_desktopNotifications = desktopNotifications;
		_weatherProvider = weatherTrigger is null ? null : new IntegrationWeatherProviderApi(weatherTrigger);
		_musicPlayerRegistry = musicPlayerRegistry;
		_musicPlayerNotifier = musicPlayerNotifier;
		_companionDevices = companionDevices;
		_storage = storage;
		_locale = locale;
		_http = http;
		_emailServices = emailServices;
		_widgetData = widgetData;
		_variableHistory = variableHistory;
		_notificationManager = notificationManager;
		_batteries = batteries;
		_batteryProviders = batteryProviders;
		_androidDevices = androidDevices;
		_uiResourceStore = uiResourceStore;
		_musicPlayerArtwork = musicPlayerArtwork;
		_albumColor = albumColor;
		_calendarServices = calendarServices;
		_messageBroker = messageBroker;
		_colorWatches = colorWatches;
		_pollingInvalidation = pollingInvalidation;
		_serviceScopeFactory = serviceScopeFactory;
		_deckNavigator = deckNavigator;
		_scriptApi = scriptApi;
		_widgetApi = widgetApi;
		_widgetIconInvalidator = widgetIconInvalidator;
		_userVariableApi = userVariableApi;
		_eventBus = eventBus;
		_bindingTracker = bindingTracker;
		_userNotificationStore = userNotificationStore;
		_bindingStore = bindingStore;
		_refreshSignal = refreshSignal;
		_hostIssueStore = hostIssueStore;
		_layoutProviders = layoutProviders;
		_folderViewProviders = folderViewProviders;
		_screenSaverProviders = screenSaverProviders;
		_videoStreamProviders = videoStreamProviders;
		_widgetTypeProviders = widgetTypeProviders;
		_deviceProviders = deviceProviders;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public IReadOnlySet<string> AttemptedIds => _attempted.Keys.ToHashSet(StringComparer.Ordinal);

	public void BindGateways(IIntegration integration)
	{
		IntegrationGatewayBinder.Bind(integration, _calendarServices, _emailServices);
	}

	public async Task<IntegrationInitializationOutcome> InitializeAsync(
		IIntegration integration,
		string integrationName,
		CancellationToken cancellationToken = default)
	{
		var scope = _serviceScopeFactory.CreateAsyncScope();
		var variableService = scope.ServiceProvider.GetRequiredService<IVariableService>();
		var mcpWriteGuard = scope.ServiceProvider.GetService<IMcpVariableWriteGuard>();

		var variableApi = new IntegrationVariableApi(integration.Id,
			variableService,
			_refreshSignal,
			_pollingInvalidation,
			_variableHistory,
			new VariableBindingAppender(_bindingStore, _timeProvider),
			mcpWriteGuard);
		var widgetApi = new IntegrationWidgetApi(integration.Id, _widgetApi, _widgetIconInvalidator, _widgetData);
		var config = new IntegrationConfig(integration.Id, _serviceScopeFactory);

		var events = _eventPublishers.GetOrAdd(integration.Id,
			id => new IntegrationEventPublisher(id, _eventBus, _bindingTracker, _logger));

		var context = new IntegrationContext(variableApi,
			mcpWriteGuard is null
				? _userVariableApi
				: new McpGuardedUserVariableApi(integration.Id, _userVariableApi, mcpWriteGuard),
			config,
			_deckNavigator,
			_scriptApi,
			widgetApi,
			events,
			new IntegrationUserNotifier(integration.Id, integrationName, _userNotificationStore, _logger),
			await RenewMessageChannelAsync(integration.Id),
			await RenewColorApiAsync(integration.Id),
			_notificationManager is null
				? UnavailableNotificationService.Instance
				: new OwnedNotificationService(new NotificationOwner(NotificationSourceCategory.BuiltInIntegration,
						integration.Id,
						integrationName),
					_notificationManager),
			UiResourcesFor(integration),
			RenewBatteryApi(integration.Id),
			_androidDevices,
			_weatherProvider,
			_storage?.For(integration.Id),
			_locale,
			_http,
			_companionDevices,
			RenewMusicPlayerApi(integration.Id),
			_desktopNotifications);

		// IIntegration.IsInitialized has no setter, so it is the SDK's own word on whether initialization
		// finished, not on whether it was attempted. Shutdown needs the latter to avoid leaking whatever a
		// timed-out integration already acquired, so the id is recorded here, before the call, regardless
		// of how InitializeAsync ends up completing.
		_attempted[integration.Id] = 0;

		// Task.Run here is load-bearing, not stylistic. Task.WhenAll (used by the caller across all pending
		// integrations) eagerly enumerates its argument, and an integration is free to block its calling
		// thread before it ever returns a Task - that is literally the reported Spotify shape. Awaiting
		// InitializeAsync directly would let such an integration block the fan-out itself on the enumerating
		// thread, and no timeout below would ever get the chance to fire. Wrapping the call in Task.Run moves
		// that blocking prologue onto a pool thread so the timeout always gets to run concurrently with it.
		// BindGateways runs integration-authored code too (UseGateway), so it goes inside the same Task.Run.
		var attempt = Task.Run(() =>
			{
				BindGateways(integration);
				return integration.InitializeAsync(context);
			},
			CancellationToken.None);

		try
		{
			await attempt.WaitAsync(_initializeTimeout, _timeProvider, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			_logger.Information(
				"Integration '{IntegrationId}' initialization was abandoned because the host is shutting down",
				integration.Id);
			ObserveAbandoned(attempt, scope, integration.Id);
			await ReleaseMessagingAsync(integration.Id);
			return IntegrationInitializationOutcome.Aborted;
		}
		catch (TimeoutException)
		{
			_logger.Error(
				"Integration '{IntegrationId}' did not finish initializing within {Timeout} and was abandoned",
				integration.Id,
				_initializeTimeout);
			_hostIssueStore.RaiseStartupTimeout(integration.Id);
			ObserveAbandoned(attempt, scope, integration.Id);
			await ReleaseMessagingAsync(integration.Id);
			return IntegrationInitializationOutcome.TimedOut;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to initialize integration '{IntegrationId}'", integration.Id);
			_hostIssueStore.RaiseStartupFailure(integration.Id, ex.Message);
			await scope.DisposeAsync();
			await ReleaseMessagingAsync(integration.Id);
			return IntegrationInitializationOutcome.Failed;
		}

		_hostIssueStore.Clear(integration.Id);
		_logger.Information("Integration '{IntegrationId}' initialized successfully", integration.Id);
		// Widget types before layouts, folder views and devices: a folder view or a virtual profile may
		// name one of the integration's own widget types.
		await _widgetTypeProviders.StartAsync(integration, cancellationToken);
		// Layouts before devices: a device registering in the same InitializeAsync call needs its
		// layout already resolvable to pick up a snapshot on its first pass.
		await _layoutProviders.StartAsync(integration, cancellationToken);
		await _folderViewProviders.StartAsync(integration, cancellationToken);
		await _screenSaverProviders.StartAsync(integration, cancellationToken);
		await _videoStreamProviders.StartAsync(integration, cancellationToken);
		await _deviceProviders.StartAsync(integration, cancellationToken);
		if (_batteryProviders is not null)
		{
			await _batteryProviders.StartAsync(integration, integrationName, cancellationToken);
		}

		await scope.DisposeAsync();
		return IntegrationInitializationOutcome.Initialized;
	}

	public async Task ReleaseMessagingAsync(string integrationId)
	{
		_notificationManager?.CloseOwner(integrationId);

		if (_messageChannels.TryRemove(integrationId, out var channel))
		{
			await channel.DisposeAsync();
		}

		if (_colorApis.TryRemove(integrationId, out var colors))
		{
			await colors.DisposeAsync();
		}

		if (_batteryApis.TryRemove(integrationId, out var batteries))
		{
			batteries.Dispose();
		}

		if (_musicPlayerApis.TryRemove(integrationId, out var musicPlayers))
		{
			musicPlayers.Dispose();
		}
	}

	public void ReleaseUiResources(string integrationId)
	{
		if (_uiResources.TryRemove(integrationId, out var resources))
		{
			resources.Release();
		}
	}

	public void ReleaseAllUiResources()
	{
		foreach (var integrationId in _uiResources.Keys)
		{
			ReleaseUiResources(integrationId);
		}
	}

	private BuiltInUiResources? UiResourcesFor(IIntegration integration)
	{
		return _uiResourceStore is null
			? null
			: _uiResources.GetOrAdd(integration.Id,
				id => new BuiltInUiResources(id, _uiResourceStore, integration.GetType(), _musicPlayerArtwork));
	}

	public async Task ReleaseAllMessagingAsync()
	{
		foreach (var integrationId in _messageChannels.Keys)
		{
			await ReleaseMessagingAsync(integrationId);
		}
	}

	private async Task<InProcessColorApi> RenewColorApiAsync(string integrationId)
	{
		if (_colorApis.TryRemove(integrationId, out var previous))
		{
			await previous.DisposeAsync();
		}

		var colors = new InProcessColorApi(_colorWatches, _logger);
		_colorApis[integrationId] = colors;
		return colors;
	}

	private InProcessBatteryApi? RenewBatteryApi(string integrationId)
	{
		if (_batteryApis.TryRemove(integrationId, out var previous))
		{
			previous.Dispose();
		}

		if (_batteries is null)
		{
			return null;
		}

		var batteries = new InProcessBatteryApi(_batteries, _logger);
		_batteryApis[integrationId] = batteries;
		return batteries;
	}

	private InProcessMusicPlayerApi? RenewMusicPlayerApi(string integrationId)
	{
		if (_musicPlayerApis.TryRemove(integrationId, out var previous))
		{
			previous.Dispose();
		}

		if (_musicPlayerRegistry is null || _albumColor is null || _musicPlayerNotifier is null)
		{
			return null;
		}

		var musicPlayers
			= new InProcessMusicPlayerApi(_musicPlayerRegistry, _albumColor, _musicPlayerNotifier, _logger);
		_musicPlayerApis[integrationId] = musicPlayers;
		return musicPlayers;
	}

	private async Task<InProcessMessageChannel> RenewMessageChannelAsync(string integrationId)
	{
		await ReleaseMessagingAsync(integrationId);
		var channel = new InProcessMessageChannel(_messageBroker, integrationId, _logger);
		_messageChannels[integrationId] = channel;
		return channel;
	}

	private void ObserveAbandoned(Task attempt, AsyncServiceScope scope, string integrationId)
	{
		_ = ContinueAbandoned(attempt, scope, integrationId);
	}

	private async Task ContinueAbandoned(Task attempt, AsyncServiceScope scope, string integrationId)
	{
		try
		{
			await attempt;
		}
		catch (Exception ex)
		{
			_logger.Error(ex,
				"Integration '{IntegrationId}' failed after its initialization was abandoned",
				integrationId);
		}
		finally
		{
			// The abandoned task keeps using services from this scope for as long as it keeps running, so
			// disposal is deliberately deferred to here instead of happening where the timeout/cancellation
			// was detected - tearing the scope down earlier would pull services out from under a hung
			// InitializeAsync that is still executing on its own pool thread.
			await scope.DisposeAsync();
		}
	}
}
