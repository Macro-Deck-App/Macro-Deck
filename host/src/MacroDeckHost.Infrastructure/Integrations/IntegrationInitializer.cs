using System.Collections.Concurrent;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Variables;
using MacroDeckHost.Infrastructure.Widgets;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Companion;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Decks;
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
	private readonly IAdbGateway _adbGateway;
	private readonly ICompanionGateway _companionGateway;
	private readonly IVariableBindingStore _bindingStore;
	private readonly IVariableRefreshSignal _refreshSignal;
	private readonly IIntegrationHostIssueStore _hostIssueStore;
	private readonly LayoutProviderHost _layoutProviders;
	private readonly FolderViewProviderHost _folderViewProviders;
	private readonly ScreenSaverProviderHost _screenSaverProviders;
	private readonly WidgetTypeProviderHost _widgetTypeProviders;
	private readonly DeviceProviderHost _deviceProviders;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly IKnownAudioDeviceStore? _knownAudioDevices;
	private readonly IVariablePollingInvalidationSignal? _pollingInvalidation;

	private readonly ConcurrentDictionary<string, byte> _attempted = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, IntegrationEventPublisher> _eventPublishers = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, InProcessMessageChannel> _messageChannels = new(StringComparer.Ordinal);
	private readonly IMessageBroker _messageBroker;

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
		IAdbGateway adbGateway,
		ICompanionGateway companionGateway,
		IVariableBindingStore bindingStore,
		IVariableRefreshSignal refreshSignal,
		IIntegrationHostIssueStore hostIssueStore,
		LayoutProviderHost layoutProviders,
		FolderViewProviderHost folderViewProviders,
		WidgetTypeProviderHost widgetTypeProviders,
		ScreenSaverProviderHost screenSaverProviders,
		DeviceProviderHost deviceProviders,
		TimeProvider timeProvider,
		ILogger logger,
		IMessageBroker messageBroker,
		IKnownAudioDeviceStore? knownAudioDevices = null,
		IVariablePollingInvalidationSignal? pollingInvalidation = null)
	{
		_messageBroker = messageBroker;
		_knownAudioDevices = knownAudioDevices;
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
		_adbGateway = adbGateway;
		_companionGateway = companionGateway;
		_bindingStore = bindingStore;
		_refreshSignal = refreshSignal;
		_hostIssueStore = hostIssueStore;
		_layoutProviders = layoutProviders;
		_folderViewProviders = folderViewProviders;
		_screenSaverProviders = screenSaverProviders;
		_widgetTypeProviders = widgetTypeProviders;
		_deviceProviders = deviceProviders;
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public IReadOnlySet<string> AttemptedIds => _attempted.Keys.ToHashSet(StringComparer.Ordinal);

	public void BindGateways(IIntegration integration)
		=> IntegrationGatewayBinder.Bind(integration,
			_adbGateway,
			_companionGateway,
			_bindingStore,
			_refreshSignal,
			_knownAudioDevices,
			_pollingInvalidation);

	public async Task<IntegrationInitializationOutcome> InitializeAsync(
		IIntegration integration,
		string integrationName,
		CancellationToken cancellationToken = default)
	{
		var scope = _serviceScopeFactory.CreateAsyncScope();
		var variableService = scope.ServiceProvider.GetRequiredService<IVariableService>();

		var variableApi = new IntegrationVariableApi(integration.Id, variableService);
		var widgetApi = new IntegrationWidgetApi(integration.Id, _widgetApi, _widgetIconInvalidator);
		var config = new IntegrationConfig(integration.Id, _serviceScopeFactory);

		var events = _eventPublishers.GetOrAdd(integration.Id,
			id => new IntegrationEventPublisher(id, _eventBus, _bindingTracker, _logger));

		var context = new IntegrationContext(variableApi,
			_userVariableApi,
			config,
			_deckNavigator,
			_scriptApi,
			widgetApi,
			events,
			new IntegrationUserNotifier(integration.Id, integrationName, _userNotificationStore, _logger),
			await RenewMessageChannelAsync(integration.Id));

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
		await _deviceProviders.StartAsync(integration, cancellationToken);
		await scope.DisposeAsync();
		return IntegrationInitializationOutcome.Initialized;
	}

	public async Task ReleaseMessagingAsync(string integrationId)
	{
		if (_messageChannels.TryRemove(integrationId, out var channel))
		{
			await channel.DisposeAsync();
		}
	}

	public async Task ReleaseAllMessagingAsync()
	{
		foreach (var integrationId in _messageChannels.Keys)
		{
			await ReleaseMessagingAsync(integrationId);
		}
	}

	private async Task<InProcessMessageChannel> RenewMessageChannelAsync(string integrationId)
	{
		await ReleaseMessagingAsync(integrationId);
		var channel = new InProcessMessageChannel(_messageBroker, integrationId, _logger);
		_messageChannels[integrationId] = channel;
		return channel;
	}

	private void ObserveAbandoned(Task attempt, AsyncServiceScope scope, string integrationId)
		=> _ = ContinueAbandoned(attempt, scope, integrationId);

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
