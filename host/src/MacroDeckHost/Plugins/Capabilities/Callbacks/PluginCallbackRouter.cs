using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.FolderViews;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Devices;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Variables;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Variables;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginCallbackRouter : IPluginCallbackRouter
{
	private const int MaxConcurrentIconTransfers = 4;

	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUserNotificationStore _notificationStore;
	private readonly IDeckNavigator _deckNavigator;
	private readonly IScriptApi _scriptApi;
	private readonly IWidgetApi _widgetApi;
	private readonly IWidgetIconInvalidator _widgetIconInvalidator;
	private readonly IUserVariableApi _userVariableApi;
	private readonly IActionInteractions _actionInteractions;
	private readonly IUiSessionSink _uiSessions;
	private readonly IPluginDeviceRegistry _deviceRegistry;
	private readonly ILayoutRegistry _layoutRegistry;
	private readonly IFolderViewRegistry _folderViewRegistry;
	private readonly IWidgetTypeRegistry _widgetTypeRegistry;
	private readonly IModalInteractionCoordinator _modals;
	private readonly IUiTransport _transport;
	private readonly IDeviceSurfaceService? _deviceSurfaces;
	private readonly RemoteDeviceSessionRegistry? _deviceSessions;
	private readonly IPluginHostAssetSender? _assets;
	private readonly RemoteVariableSubscriptions? _variableSubscriptions;
	private readonly VariableUpdateChannel? _dynamicVariableChannel;
	private readonly VariableCatalogInvalidationSignal? _dynamicVariableInvalidation;
	private readonly HostCallbackThrottle _throttle;

	// Rate limiting alone does not bound this: an icon transfer outlives the call that started it, so a
	// plugin fetching a full deck's icons at once would otherwise hold every image, and its base64 chunk
	// copies, in memory simultaneously. Concurrent transfers per plugin are capped instead; the rest
	// queue and start as earlier ones finish.
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _iconTransfers = new(StringComparer.Ordinal);
	private readonly IHostLockState _lockState;
	private readonly ILogger _logger;

	public PluginCallbackRouter(
		IPluginSessionRegistry sessionRegistry,
		IPluginCapabilityInvoker invoker,
		IServiceScopeFactory scopeFactory,
		IUserNotificationStore notificationStore,
		IDeckNavigator deckNavigator,
		IScriptApi scriptApi,
		IWidgetApi widgetApi,
		IWidgetIconInvalidator widgetIconInvalidator,
		IUserVariableApi userVariableApi,
		IActionInteractions actionInteractions,
		IUiSessionSink uiSessions,
		IPluginDeviceRegistry deviceRegistry,
		ILayoutRegistry layoutRegistry,
		IFolderViewRegistry folderViewRegistry,
		IWidgetTypeRegistry widgetTypeRegistry,
		IModalInteractionCoordinator modals,
		IUiTransport transport,
		HostCallbackThrottle throttle,
		IHostLockState lockState,
		ILogger logger,
		IDeviceSurfaceService? deviceSurfaces = null,
		RemoteDeviceSessionRegistry? deviceSessions = null,
		IPluginHostAssetSender? assets = null,
		RemoteVariableSubscriptions? variableSubscriptions = null,
		VariableUpdateChannel? dynamicVariableChannel = null,
		VariableCatalogInvalidationSignal? dynamicVariableInvalidation = null)
	{
		_deviceSurfaces = deviceSurfaces;
		_deviceSessions = deviceSessions;
		_assets = assets;
		_variableSubscriptions = variableSubscriptions;
		_dynamicVariableChannel = dynamicVariableChannel;
		_dynamicVariableInvalidation = dynamicVariableInvalidation;
		_sessionRegistry = sessionRegistry;
		_invoker = invoker;
		_scopeFactory = scopeFactory;
		_notificationStore = notificationStore;
		_deckNavigator = deckNavigator;
		_scriptApi = scriptApi;
		_widgetApi = widgetApi;
		_widgetIconInvalidator = widgetIconInvalidator;
		_userVariableApi = userVariableApi;
		_actionInteractions = actionInteractions;
		_uiSessions = uiSessions;
		_deviceRegistry = deviceRegistry;
		_layoutRegistry = layoutRegistry;
		_folderViewRegistry = folderViewRegistry;
		_widgetTypeRegistry = widgetTypeRegistry;
		_modals = modals;
		_transport = transport;
		_throttle = throttle;
		_lockState = lockState;
		_logger = logger.ForContext<PluginCallbackRouter>();
	}

	public async Task<HostCallbackResult> RouteAsync(
		string pluginId,
		string correlationId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentNullException.ThrowIfNull(payload);

		if (!HostApis.IsKnown(payload.Api))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
				$"The host has no api '{payload.Api}'.");
		}

		if (!HostOperations.IsKnown(payload.Api, payload.Operation))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
				$"The '{payload.Api}' api has no operation '{payload.Operation}'.");
		}

		// The ui api is deliberately exempt. Its traffic is a per-session tree and patch stream whose rate
		// is already bounded, per session, by ProtocolLimits.MaxUiUpdatesPerSecond and the session's own
		// token bucket. Charging it to the shared per-plugin bucket as well would let one busy view
		// starve every other callback the plugin makes, and would drop patches under a limiter that
		// cannot ask for the resync the session limiter asks for.
		if (!string.Equals(payload.Api, HostApis.Ui, StringComparison.Ordinal) && !_throttle.TryConsume(pluginId))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.RateLimited,
				"This plugin is calling back into the host too quickly.",
				retryable: true);
		}

		try
		{
			return payload.Api switch
			{
				HostApis.Variables => await RouteVariablesAsync(pluginId, payload, cancellationToken),
				HostApis.UserVariables => await RouteUserVariablesAsync(pluginId, payload, cancellationToken),
				HostApis.Config => await RouteConfigAsync(pluginId, payload, cancellationToken),
				HostApis.Deck => await RouteDeckAsync(payload, cancellationToken),
				HostApis.Scripts => await RouteScriptsAsync(payload, cancellationToken),
				HostApis.Widgets => await RouteWidgetsAsync(pluginId, payload, cancellationToken),
				HostApis.Notifications => RouteNotifications(pluginId, payload),
				HostApis.ActionInteractions => RouteActionInteractions(pluginId, correlationId, payload),
				HostApis.Ui => RouteUi(pluginId, payload),
				HostApis.Devices => await RouteDevicesAsync(pluginId, payload, cancellationToken),
				HostApis.VariableValues => RouteVariableValues(pluginId, payload),
				HostApis.Layouts => await RouteLayoutsAsync(pluginId, payload, cancellationToken),
				HostApis.FolderViews => await RouteFolderViewsAsync(pluginId, payload, cancellationToken),
				HostApis.WidgetTypes => await RouteWidgetTypesAsync(pluginId, payload, cancellationToken),
				_ => HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
					$"The host has no api '{payload.Api}'.")
			};
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// A plugin is untrusted code the same way a capability handler is, so a failure inside a
			// host service becomes a result rather than a torn-down socket. The message is not passed
			// through - it may carry a path, a connection string, or other host internals.
			PluginCallbackRouterLog.CallbackFailed(_logger, payload.Api, payload.Operation, pluginId, exception);

			return HostCallbackResult.Fail(ProtocolErrorCodes.InternalError,
				ProtocolErrorMessages.For(ProtocolErrorCodes.InternalError));
		}
	}

	private async Task<HostCallbackResult> RouteVariablesAsync(string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var variableApi
			= new IntegrationVariableApi(pluginId, scope.ServiceProvider.GetRequiredService<IVariableService>());

		switch (payload.Operation)
		{
			case HostOperations.Variables.List:
				return HostCallbackResult.Ok(await variableApi.GetAllAsync());

			case HostOperations.Variables.Get:
			{
				var arguments = Deserialize<VariablesGetArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				return HostCallbackResult.Ok(await variableApi.GetByNameAsync(arguments.Name));
			}

			case HostOperations.Variables.Create:
			{
				var arguments = Deserialize<VariablesCreateArguments>(payload.Arguments);
				if (arguments is null || !Enum.TryParse<VariableType>(arguments.Type, out var type))
				{
					return MissingArguments();
				}

				var created = await variableApi.CreateAsync(arguments.Name,
					type,
					arguments.InitialValue,
					arguments.DecimalPlaces,
					arguments.DefinitionId);

				return HostCallbackResult.Ok(created);
			}

			case HostOperations.Variables.Set:
			{
				if (_lockState.IsLocked)
				{
					return HostLocked();
				}

				var arguments = Deserialize<VariablesSetArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await variableApi.SetValueAsync(arguments.VariableId, arguments.Value);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Variables.Delete:
			{
				var arguments = Deserialize<VariablesDeleteArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await variableApi.DeleteAsync(arguments.VariableId);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private async Task<HostCallbackResult> RouteUserVariablesAsync(string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (_lockState.IsLocked)
		{
			return HostLocked();
		}

		_ = pluginId;

		switch (payload.Operation)
		{
			case HostOperations.UserVariables.Apply:
			{
				var arguments = Deserialize<UserVariablesApplyArguments>(payload.Arguments);
				if (arguments is null ||
					!Enum.TryParse<UserVariableOperation>(arguments.Operation, out var operation))
				{
					return MissingArguments();
				}

				var result = await _userVariableApi.ApplyAsync(arguments.Name,
					arguments.OwnerWidgetId,
					operation,
					arguments.Value,
					cancellationToken);

				return HostCallbackResult.Ok(result);
			}

			case HostOperations.UserVariables.Create:
			{
				var arguments = Deserialize<UserVariablesCreateArguments>(payload.Arguments);
				if (arguments is null || !Enum.TryParse<VariableType>(arguments.Type, out var type))
				{
					return MissingArguments();
				}

				var result = await _userVariableApi.CreateAsync(arguments.Name,
					arguments.OwnerWidgetId,
					type,
					arguments.InitialValue,
					arguments.DecimalPlaces,
					cancellationToken);

				return HostCallbackResult.Ok(result);
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private async Task<HostCallbackResult> RouteConfigAsync(string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		var config = new IntegrationConfig(pluginId, _scopeFactory);

		switch (payload.Operation)
		{
			case HostOperations.Config.Entries:
				return HostCallbackResult.Ok(await config.GetEntriesAsync(cancellationToken));

			case HostOperations.Config.GetString:
			{
				var arguments = Deserialize<ConfigGetArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				return HostCallbackResult.Ok(await config.GetStringAsync(arguments.EntryId,
					arguments.Key,
					cancellationToken));
			}

			case HostOperations.Config.GetSecret:
			{
				var arguments = Deserialize<ConfigGetArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				return HostCallbackResult.Ok(await config.GetSecretAsync(arguments.EntryId,
					arguments.Key,
					cancellationToken));
			}

			case HostOperations.Config.SetString:
			{
				var arguments = Deserialize<ConfigSetStringArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await config.SetStringAsync(arguments.EntryId, arguments.Key, arguments.Value, cancellationToken);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Config.SetSecret:
			{
				var arguments = Deserialize<ConfigSetSecretArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await config.SetSecretAsync(arguments.EntryId, arguments.Key, arguments.Value, cancellationToken);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private async Task<HostCallbackResult> RouteDeckAsync(HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (_lockState.IsLocked)
		{
			return HostLocked();
		}

		switch (payload.Operation)
		{
			case HostOperations.Deck.ChangeFolder:
			{
				var arguments = Deserialize<DeckChangeFolderArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await _deckNavigator.ChangeFolderAsync(arguments.FolderId,
					ClientOrigin(arguments.OriginClientId),
					cancellationToken);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Deck.ChangeProfile:
			{
				var arguments = Deserialize<DeckChangeProfileArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await _deckNavigator.ChangeProfileAsync(arguments.ProfileId,
					ClientOrigin(arguments.OriginClientId),
					cancellationToken);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Deck.Parent:
			{
				var arguments = Deserialize<DeckOriginArguments>(payload.Arguments) ?? new DeckOriginArguments();
				await _deckNavigator.GoToParentAsync(ClientOrigin(arguments.OriginClientId), cancellationToken);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Deck.Back:
			{
				var arguments = Deserialize<DeckOriginArguments>(payload.Arguments) ?? new DeckOriginArguments();
				await _deckNavigator.GoBackAsync(ClientOrigin(arguments.OriginClientId), cancellationToken);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private async Task<HostCallbackResult> RouteScriptsAsync(HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (payload.Operation != HostOperations.Scripts.Run)
		{
			return UnknownOperation(payload);
		}

		if (_lockState.IsLocked)
		{
			return HostLocked();
		}

		var arguments = Deserialize<ScriptsRunArguments>(payload.Arguments);
		if (arguments is null)
		{
			return MissingArguments();
		}

		var result = await _scriptApi.RunAsync(arguments.ScriptId,
			arguments.Inputs,
			ClientOrigin(arguments.OriginClientId),
			arguments.OwnerWidgetId,
			cancellationToken);
		return HostCallbackResult.Ok(result);
	}

	private async Task<HostCallbackResult> RouteWidgetsAsync(string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (payload.Operation == HostOperations.Widgets.InvalidateIcon)
		{
			var invalidateArguments = Deserialize<WidgetsInvalidateIconArguments>(payload.Arguments);
			if (invalidateArguments is null)
			{
				return MissingArguments();
			}

			// pluginId comes from the connection, never from the payload - a plugin can only ever
			// invalidate its own actions.
			_widgetIconInvalidator.Invalidate(pluginId, invalidateArguments.ActionId);
			return HostCallbackResult.Ok();
		}

		if (payload.Operation != HostOperations.Widgets.Apply)
		{
			return UnknownOperation(payload);
		}

		var negotiatedVersion = _sessionRegistry.GetNegotiatedVersion(pluginId);
		WidgetAppearanceRequest request;

		if (negotiatedVersion is >= 2)
		{
			var arguments = Deserialize<WidgetsApplyArgumentsV2>(payload.Arguments);
			if (arguments is null)
			{
				return MissingArguments();
			}

			request = WidgetStateWireCompatibility.ToApplyRequest(arguments);
		}
		else
		{
			var arguments = Deserialize<WidgetsApplyArgumentsV1>(payload.Arguments);
			if (arguments is null)
			{
				return MissingArguments();
			}

			var targetStates = _widgetApi.GetWidgets()
					.FirstOrDefault(widget => string.Equals(widget.Id, arguments.WidgetId, StringComparison.Ordinal))
					?.States ??
				[];

			request = WidgetStateWireCompatibility.ToApplyRequest(arguments, targetStates);
		}

		var applied = await _widgetApi.ApplyAsync(request, cancellationToken);
		return HostCallbackResult.Ok(applied);
	}

	private HostCallbackResult RouteNotifications(string pluginId, HostInvokePayload payload)
	{
		var displayName = DisplayNameOf(pluginId);
		var notifier = new IntegrationUserNotifier(pluginId,
			displayName,
			_notificationStore,
			Serilog.Log.Logger);

		switch (payload.Operation)
		{
			case HostOperations.Notifications.Notify:
			{
				var request = Deserialize<UserNotificationRequest>(payload.Arguments);
				if (request is null)
				{
					return MissingArguments();
				}

				notifier.Notify(request);
				return HostCallbackResult.Ok();
			}

			case HostOperations.Notifications.Dismiss:
			{
				var arguments = Deserialize<NotificationsDismissArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				notifier.Dismiss(arguments.Key);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private HostCallbackResult RouteActionInteractions(string pluginId, string correlationId, HostInvokePayload payload)
	{
		_ = correlationId; // the host.invoke's own id names nothing here - see ExecuteCorrelationId below.

		switch (payload.Operation)
		{
			case HostOperations.ActionInteractions.ShowModal:
			{
				var arguments = Deserialize<ActionInteractionsShowModalArguments>(payload.Arguments);
				if (arguments is null || string.IsNullOrEmpty(arguments.ViewId))
				{
					return MissingArguments();
				}

				if (!_invoker.IsLiveActionExecute(pluginId, arguments.ExecuteCorrelationId))
				{
					return NotALiveExecute();
				}

				return HostCallbackResult.Ok(ShowModal(pluginId, arguments));
			}

			case HostOperations.ActionInteractions.RequestItemPicker:
			{
				var arguments = Deserialize<ActionInteractionsRequestItemPickerArguments>(payload.Arguments);
				if (arguments is null || !Enum.TryParse<MusicPlayerCatalogItemKind>(arguments.Kind, out var kind))
				{
					return MissingArguments();
				}

				if (!_invoker.IsLiveActionExecute(pluginId, arguments.ExecuteCorrelationId))
				{
					return NotALiveExecute();
				}

				_actionInteractions.RequestItemPicker(ClientOrigin(arguments.OriginClientId),
					arguments.InstanceId,
					kind,
					arguments.Prompt);
				return HostCallbackResult.Ok();
			}

			case HostOperations.ActionInteractions.RequestDevicePicker:
			{
				var arguments = Deserialize<ActionInteractionsRequestDevicePickerArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (!_invoker.IsLiveActionExecute(pluginId, arguments.ExecuteCorrelationId))
				{
					return NotALiveExecute();
				}

				_actionInteractions.RequestDevicePicker(ClientOrigin(arguments.OriginClientId),
					arguments.InstanceId,
					arguments.StartPlayback,
					arguments.Prompt);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private ActionInteractionsShowModalResult ShowModal(
		string pluginId,
		ActionInteractionsShowModalArguments arguments)
	{
		// A device origin is not a client that can render a dialog, so ClientOrigin's null is a refusal
		// here rather than a broadcast: Register turns it into "not opened".
		var originClientId = ClientOrigin(arguments.OriginClientId);

		var modalId = _modals.Register(pluginId,
			originClientId,
			new ModalDefinition
			{
				ViewId = arguments.ViewId, Title = arguments.Title, Data = arguments.Data
			});

		if (modalId is null || originClientId is null)
		{
			return new ActionInteractionsShowModalResult { Opened = false };
		}

		_ = _transport.SendToGroup(UiClientGroups.For(originClientId),
			new UiModalOpenedEvent { ModalId = modalId, Title = arguments.Title ?? default },
			CancellationToken.None);

		if (arguments.AwaitResult)
		{
			_ = DeliverModalResultAsync(pluginId, modalId);
		}

		return new ActionInteractionsShowModalResult { Opened = true, ModalId = modalId };
	}

	// The answer is a second exchange rather than this call's own result: host.invoke carries a fixed
	// request deadline and a person is not bound by it. The wait always ends - the coordinator settles a
	// modal when its session ends and sweeps one nobody could still be waiting on - so this never leaks.
	private async Task DeliverModalResultAsync(string pluginId, string modalId)
	{
		try
		{
			var outcome = await _modals.AwaitAsync(modalId, CancellationToken.None).ConfigureAwait(false);

			await _invoker.InvokeAsync(pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.Ui,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.Ui.ModalResult,
						Arguments = JsonSerializer.SerializeToElement(new UiModalResultArguments
							{
								ModalId = modalId,
								Cancelled = outcome.Cancelled,
								Value = outcome.Cancelled ? null : outcome.Value
							},
							PluginProtocolJson.Options)
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// The plugin's own wait is bounded by the flow it runs in, so a result that cannot be
			// delivered - a plugin that disconnected while its modal was open - costs it a cancellation,
			// not a hang.
			PluginCallbackRouterLog.CallbackFailed(_logger,
				HostApis.ActionInteractions,
				HostOperations.ActionInteractions.ShowModal,
				pluginId,
				exception);
		}
	}

	private async Task<HostCallbackResult> RouteFolderViewsAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		// The provider is the authenticated plugin, never anything the payload claims: one plugin must
		// not be able to register or withdraw a folder view in another plugin's name.
		try
		{
			switch (payload.Operation)
			{
				case HostOperations.FolderViews.Register:
				{
					var arguments = Deserialize<FolderViewsRegisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					var registration = await _folderViewRegistry.Register(pluginId,
						FolderViewDescriptorMapper.ToDescriptor(arguments.FolderView),
						cancellationToken);

					return HostCallbackResult.Ok(new FolderViewsRegisterResult
					{
						FolderViewId = registration.FolderViewId, ProviderId = registration.ProviderId
					});
				}

				case HostOperations.FolderViews.Unregister:
				{
					var arguments = Deserialize<FolderViewsUnregisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					await _folderViewRegistry.Unregister(pluginId, arguments.FolderViewId, cancellationToken);
					return HostCallbackResult.Ok();
				}

				default:
					return UnknownOperation(payload);
			}
		}
		catch (ArgumentException exception)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, exception.Message);
		}
	}

	private async Task<HostCallbackResult> RouteWidgetTypesAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		// The provider is the authenticated plugin, never anything the payload claims: one plugin must
		// not be able to register or withdraw a widget type in another plugin's name.
		try
		{
			switch (payload.Operation)
			{
				case HostOperations.WidgetTypes.Register:
				{
					var arguments = Deserialize<WidgetTypesRegisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					var registration = await _widgetTypeRegistry.Register(pluginId,
						WidgetTypeDescriptorMapper.ToDescriptor(arguments.WidgetType),
						cancellationToken);

					return HostCallbackResult.Ok(new WidgetTypesRegisterResult
					{
						WidgetTypeId = registration.WidgetTypeId, ProviderId = registration.ProviderId
					});
				}

				case HostOperations.WidgetTypes.Unregister:
				{
					var arguments = Deserialize<WidgetTypesUnregisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					await _widgetTypeRegistry.Unregister(pluginId, arguments.WidgetTypeId, cancellationToken);
					return HostCallbackResult.Ok();
				}

				default:
					return UnknownOperation(payload);
			}
		}
		catch (ArgumentException exception)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, exception.Message);
		}
	}

	private async Task<HostCallbackResult> RouteLayoutsAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		// The provider is the authenticated plugin, never anything the payload claims: one plugin must
		// not be able to register or withdraw a layout in another plugin's name.
		try
		{
			switch (payload.Operation)
			{
				case HostOperations.Layouts.Register:
				{
					var arguments = Deserialize<LayoutsRegisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					var registration = await _layoutRegistry.Register(pluginId,
						LayoutDescriptorMapper.ToDescriptor(arguments.Layout),
						cancellationToken);

					return HostCallbackResult.Ok(new LayoutsRegisterResult
					{
						LayoutId = registration.LayoutId, ProviderId = registration.ProviderId
					});
				}

				case HostOperations.Layouts.Unregister:
				{
					var arguments = Deserialize<LayoutsUnregisterArguments>(payload.Arguments);
					if (arguments is null)
					{
						return MissingArguments();
					}

					await _layoutRegistry.Unregister(pluginId, arguments.LayoutId, cancellationToken);
					return HostCallbackResult.Ok();
				}

				default:
					return UnknownOperation(payload);
			}
		}
		catch (ArgumentException exception)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, exception.Message);
		}
	}

	private async Task<HostCallbackResult> RouteDevicesAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		// The provider is the authenticated plugin, never anything the payload claims: one plugin must
		// not be able to register, move or withdraw a device in another plugin's name.
		try
		{
			return await RouteDevicesCoreAsync(pluginId, payload, cancellationToken);
		}
		catch (ArgumentException exception)
		{
			// A malformed registration is the plugin's mistake to fix, so it is told what was wrong
			// rather than being handed the generic internal-error reply the catch-all would produce.
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, exception.Message);
		}
		catch (DeviceSessionException exception)
		{
			return HostCallbackResult.Fail(ToProtocolCode(exception.ReasonCode), exception.Message);
		}
	}

	private async Task<HostCallbackResult> RouteDevicesCoreAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.Devices.Register:
			{
				var arguments = Deserialize<DevicesRegisterArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (InvalidDevice(arguments) is { } invalid)
				{
					return invalid;
				}

				var registration = await _deviceRegistry.RegisterAsync(pluginId,
					DeviceDescriptorMapper.ToDescriptor(arguments.Device),
					cancellationToken);

				return HostCallbackResult.Ok(new DevicesRegisterResult
				{
					DeviceId = registration.DeviceId, ProviderDeviceId = registration.ProviderDeviceId
				});
			}

			case HostOperations.Devices.Update:
			{
				var arguments = Deserialize<DevicesRegisterArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (InvalidDevice(arguments) is { } invalid)
				{
					return invalid;
				}

				await _deviceRegistry.UpdateAsync(pluginId,
					DeviceDescriptorMapper.ToDescriptor(arguments.Device),
					cancellationToken);

				return HostCallbackResult.Ok();
			}

			case HostOperations.Devices.Presence:
			{
				var arguments = Deserialize<DevicesPresenceArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await _deviceRegistry.SetPresenceAsync(pluginId,
					arguments.DeviceId,
					DeviceDescriptorMapper.ToPresence(arguments.Presence),
					cancellationToken);

				return HostCallbackResult.Ok();
			}

			case HostOperations.Devices.Unregister:
			{
				var arguments = Deserialize<DevicesUnregisterArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				await _deviceRegistry.UnregisterAsync(pluginId, arguments.DeviceId, cancellationToken);

				return HostCallbackResult.Ok();
			}

			case HostOperations.Devices.Interaction:
			{
				var arguments = Deserialize<DevicesInteractionArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (ResolveSession(pluginId, arguments.SessionId) is not { } deviceId)
				{
					return UnknownSession();
				}

				var outcome = await _deviceSurfaces!.SubmitInteractionAsync(deviceId,
					new DeviceInteraction
					{
						Kind = ToInteractionKind(arguments.Kind),
						Target = new DeviceInteractionTarget
						{
							WidgetId = arguments.WidgetId, ControlIndex = arguments.ControlIndex
						},
						Value = arguments.Value,
						SurfaceRevision = arguments.SurfaceRevision,
						Data = arguments.Data
					},
					cancellationToken);

				// A refusal is a verdict, not a protocol error: it comes back as a result so a remote
				// provider reads the same outcome an in-process one gets from the surface service.
				return HostCallbackResult.Ok(new DevicesInteractionResult
				{
					Accepted = outcome.ErrorCode is null,
					Unsupported = outcome.Unsupported,
					ReasonCode = outcome.ErrorCode
				});
			}

			case HostOperations.Devices.Icon:
			{
				var arguments = Deserialize<DevicesIconArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (ResolveSession(pluginId, arguments.SessionId) is not { } deviceId)
				{
					return UnknownSession();
				}

				// Parsed here rather than deeper: the icon id is a plugin-supplied string, and everything
				// below this point treats it as an identity it may look up.
				if (!Guid.TryParse(arguments.IconId, out _))
				{
					return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
						"An icon id must be a GUID.");
				}

				var icon = await _deviceSurfaces!.GetIconAsync(deviceId,
					arguments.IconId,
					arguments.Size,
					arguments.KnownETag,
					cancellationToken);

				if (icon is null)
				{
					return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
						$"No icon '{arguments.IconId}' is available to this device session.");
				}

				return IconResult(pluginId, icon);
			}

			case HostOperations.Devices.WidgetIcon:
			{
				var arguments = Deserialize<DevicesWidgetIconArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (ResolveSession(pluginId, arguments.SessionId) is not { } deviceId)
				{
					return UnknownSession();
				}

				var icon = await _deviceSurfaces!.GetWidgetIconAsync(deviceId,
					arguments.WidgetId,
					arguments.KnownETag,
					cancellationToken);

				// Unlike the icon-pack Icon case above, nothing to serve for this widget right now is
				// routine churn - the provider went inactive, answered blank, or the id fell off the
				// surface between push and fetch - not a protocol violation, so it is answered with no
				// value rather than a failure.
				return icon is null ? HostCallbackResult.Ok() : WidgetIconResult(pluginId, icon);
			}

			case HostOperations.Devices.Close:
			{
				var arguments = Deserialize<DevicesCloseArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (ResolveSession(pluginId, arguments.SessionId) is not { } deviceId)
				{
					return UnknownSession();
				}

				await _deviceSurfaces!.CloseAsync(deviceId, reason: null, cancellationToken);

				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	private static HostCallbackResult? InvalidDevice(DevicesRegisterArguments arguments)
	{
		if (string.IsNullOrWhiteSpace(arguments.Device.Id))
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
				"A device registration needs a stable, non-empty device id.");
		}

		return string.IsNullOrWhiteSpace(arguments.Device.Name)
			? HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
				"A device registration needs a non-empty name.")
			: null;
	}

	private HostCallbackResult RouteUi(string pluginId, HostInvokePayload payload)
	{
		switch (payload.Operation)
		{
			case HostOperations.Ui.Snapshot:
			{
				var arguments = Deserialize<UiSnapshotArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				return FromIngest(_uiSessions.PublishSnapshot(pluginId,
					arguments.SessionId,
					UiRawJson.FromElement(arguments.Tree)));
			}

			case HostOperations.Ui.Patch:
			{
				var arguments = Deserialize<UiPatchArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				return FromIngest(_uiSessions.PublishPatch(pluginId,
					arguments.SessionId,
					UiRawJson.FromElement(arguments.Patch)));
			}

			case HostOperations.Ui.Fault:
			{
				var arguments = Deserialize<UiFaultArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				_uiSessions.PublishFault(pluginId, arguments.SessionId, arguments.Code, arguments.Message);
				return HostCallbackResult.Ok();
			}

			default:
				return UnknownOperation(payload);
		}
	}

	/// <summary>
	/// Handles the <c>variable-values</c> host-api callbacks a push-capable provider makes through
	/// <c>IVariableSink</c>. <c>value</c> drops any id not currently subscribed <em>for this plugin</em>
	/// per <see cref="RemoteVariableSubscriptions" /> before it ever reaches the update channel - an
	/// untrusted process must never be able to write a value into another plugin's variable, or into a
	/// resource nobody is watching. A dropped id is a race between the two ends resubscribing, not a
	/// fault, so this always answers <c>Ok</c>.
	/// </summary>
	private HostCallbackResult RouteVariableValues(string pluginId, HostInvokePayload payload)
	{
		switch (payload.Operation)
		{
			case HostOperations.VariableValues.Value:
			{
				var arguments = Deserialize<VariableValuesValueArguments>(payload.Arguments);
				if (arguments is null)
				{
					return MissingArguments();
				}

				if (arguments.Values.Count > ProtocolLimits.MaxVariableValuesPerBatch)
				{
					return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
						$"A variable-values 'value' batch may not exceed " +
						$"{ProtocolLimits.MaxVariableValuesPerBatch} values.");
				}

				if (_dynamicVariableChannel is not null)
				{
					foreach (var value in arguments.Values)
					{
						if (_variableSubscriptions?.IsSubscribed(pluginId, value.Id) != true)
						{
							continue;
						}

						_dynamicVariableChannel.Write(pluginId,
							value.Id,
							VariableValueMapper.ToDomain(value.Reading.Value));
					}
				}

				return HostCallbackResult.Ok();
			}

			case HostOperations.VariableValues.Invalidate:
				_dynamicVariableInvalidation?.RaiseInvalidated(pluginId);
				return HostCallbackResult.Ok();

			default:
				return UnknownOperation(payload);
		}
	}

	// The ingest verdict is the host.result, so a provider learns from its own reply that a payload was
	// refused - the deterministic signal a relay that silently drops updates cannot give. Translated
	// rather than forwarded: UiSessionErrorCodes is the host-to-client vocabulary, and only
	// ProtocolErrorCodes is a promise to a plugin.
	private static HostCallbackResult FromIngest(UiSessionIngestResult result)
	{
		if (result.Accepted)
		{
			return HostCallbackResult.Ok();
		}

		var code = result.Code switch
		{
			UiSessionErrorCodes.SessionNotFound => ProtocolErrorCodes.SessionNotFound,
			UiSessionErrorCodes.PayloadTooLarge => ProtocolErrorCodes.PayloadTooLarge,
			UiSessionErrorCodes.RateLimited => ProtocolErrorCodes.RateLimited,
			_ => ProtocolErrorCodes.InvalidPayload
		};

		return HostCallbackResult.Fail(code, result.Message ?? "The host refused this UI payload.");
	}

	private string DisplayNameOf(string pluginId)
		=> _sessionRegistry.Snapshot()
				.FirstOrDefault(session => string.Equals(session.PluginId, pluginId, StringComparison.Ordinal))
				?.DisplayName ??
			pluginId;

	/// <summary>
	/// Answers the icon call with metadata only and starts the bytes on their way over the
	/// <c>host.asset.*</c> channel. The bytes cannot ride the reply: a protocol message is capped at
	/// <see cref="ProtocolLimits.MaxMessageBytes" /> and an icon is not. A not-modified answer starts no
	/// transfer at all, which is what makes a cache hit cost zero bytes.
	/// </summary>
	private HostCallbackResult IconResult(string pluginId, DeviceIconImage icon)
	{
		if (icon.NotModified)
		{
			return HostCallbackResult.Ok(new DevicesIconResult
			{
				ContentType = icon.ContentType,
				ETag = icon.ETag,
				ByteLength = 0,
				NotModified = true
			});
		}

		if (_assets is null)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"This host cannot transfer icon bytes to a plugin.");
		}

		var content = icon.Content;
		var transferId = Guid.CreateVersion7().ToString();
		var contentHash = AssetContentHash.Compute(content.Span);

		// Started before the reply this result becomes is sent, and deliberately not awaited: the plugin
		// correlates the transfer by its id rather than by arrival order, and awaiting a multi-megabyte
		// transfer here would hold up the very reply that announces it. Its own lifetime, not the host
		// invocation's - the invocation ends the moment its reply is written.
		var sender = _assets;
		var transfers = _iconTransfers.GetOrAdd(pluginId, _ => new SemaphoreSlim(MaxConcurrentIconTransfers));
		_ = Task.Run(async () =>
			{
				await transfers.WaitAsync(CancellationToken.None);
				try
				{
					await sender.SendAsync(pluginId,
						transferId,
						AssetKinds.Icon,
						icon.ContentType,
						content,
						contentHash,
						CancellationToken.None);
				}
				finally
				{
					transfers.Release();
				}
			},
			CancellationToken.None);

		return HostCallbackResult.Ok(new DevicesIconResult
		{
			ContentType = icon.ContentType,
			ETag = icon.ETag,
			ByteLength = content.Length,
			ContentHash = contentHash,
			NotModified = false,
			TransferId = transferId
		});
	}

	/// <summary>
	/// The provider-icon sibling of <see cref="IconResult" />, differing only in the asset kind and the
	/// result shape: an action-icon-provider image is never mistaken for a plugin's own catalog icon (see
	/// <see cref="AssetKinds.ActionIcon" />'s remarks), and it shares the same per-plugin transfer bound
	/// because both are, at bottom, icon bytes queued for the same device session.
	/// </summary>
	private HostCallbackResult WidgetIconResult(string pluginId, DeviceWidgetIconImage icon)
	{
		if (icon.NotModified)
		{
			return HostCallbackResult.Ok(new DevicesWidgetIconResult
			{
				ContentType = icon.ContentType,
				ETag = icon.ETag,
				ByteLength = 0,
				NotModified = true
			});
		}

		if (_assets is null)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"This host cannot transfer icon bytes to a plugin.");
		}

		var content = icon.Content;
		var transferId = Guid.CreateVersion7().ToString();
		var contentHash = AssetContentHash.Compute(content.Span);

		var sender = _assets;
		var transfers = _iconTransfers.GetOrAdd(pluginId, _ => new SemaphoreSlim(MaxConcurrentIconTransfers));
		_ = Task.Run(async () =>
			{
				await transfers.WaitAsync(CancellationToken.None);
				try
				{
					await sender.SendAsync(pluginId,
						transferId,
						AssetKinds.ActionIcon,
						icon.ContentType,
						content,
						contentHash,
						CancellationToken.None);
				}
				finally
				{
					transfers.Release();
				}
			},
			CancellationToken.None);

		return HostCallbackResult.Ok(new DevicesWidgetIconResult
		{
			ContentType = icon.ContentType,
			ETag = icon.ETag,
			ByteLength = content.Length,
			ContentHash = contentHash,
			NotModified = false,
			TransferId = transferId
		});
	}

	/// <summary>
	/// The device a plugin's session id addresses, and only ever one this plugin owns. The session id
	/// travelled out over <c>session.open</c> and comes back as an untrusted string, so it names a device
	/// only once the ownership record agrees - a plugin must not be able to drive another plugin's device
	/// session by quoting its id.
	/// </summary>
	private Guid? ResolveSession(string pluginId, string? sessionId)
		=> _deviceSurfaces is null || _deviceSessions is null
			? null
			: _deviceSessions.ResolveDevice(pluginId, sessionId);

	private static DeviceInteractionKind ToInteractionKind(string? kind)
		=> Enum.TryParse<DeviceInteractionKind>(kind, ignoreCase: true, out var parsed)
			? parsed
			: DeviceInteractionKind.Unknown;

	// DeviceSurfaceErrorCodes is the host's own vocabulary; only ProtocolErrorCodes is a promise to a
	// plugin - the same translation RouteUi's FromIngest performs for UI payloads.
	private static string ToProtocolCode(string code)
		=> code switch
		{
			DeviceSurfaceErrorCodes.SessionNotFound => ProtocolErrorCodes.SessionNotFound,
			DeviceSurfaceErrorCodes.HostLocked => ProtocolErrorCodes.CapabilityUnavailable,
			DeviceSessionReasons.IconTooLarge => ProtocolErrorCodes.AssetTooLarge,
			_ => ProtocolErrorCodes.InvalidPayload
		};

	private static HostCallbackResult UnknownSession()
		=> HostCallbackResult.Fail(ProtocolErrorCodes.SessionNotFound,
			"This session id does not name an open device session of this plugin.");

	// The device: prefix is minted by the host alone, for a device session acting on its own behalf. A
	// plugin-supplied one is dropped rather than honoured: it would let a plugin address a device session
	// it does not provide, and no real client ever produces this shape.
	private static string? ClientOrigin(string? originClientId)
		=> DeviceOrigin.TryParse(originClientId, out _) ? null : originClientId;

	private static HostCallbackResult MissingArguments()
		=> HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload, "This operation requires arguments.");

	private static HostCallbackResult UnknownOperation(HostInvokePayload payload)
		=> HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnsupported,
			$"The '{payload.Api}' api has no operation '{payload.Operation}'.");

	private static HostCallbackResult NotALiveExecute()
		=> HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
			"This correlation does not name a live actions/execute invocation of this plugin.");

	// event.publish (a state report) is not routed through host.invoke, so it is unaffected - only the
	// command operations checked above are refused while the host is locked.
	private static HostCallbackResult HostLocked()
		=> HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable, "The host is locked.");

	private static T? Deserialize<T>(JsonElement? element)
	{
		if (element is not { } value)
		{
			return default;
		}

		try
		{
			return value.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return default;
		}
	}
}
