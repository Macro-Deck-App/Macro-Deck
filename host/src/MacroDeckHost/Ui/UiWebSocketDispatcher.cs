using System.Security.Claims;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Logging;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Ui;

public sealed class UiWebSocketDispatcher : IDisposable
{
	private readonly string _connectionId;
	private readonly ClaimsPrincipal _principal;
	private readonly CancellationToken _connectionCancellation;
	private readonly Action _abort;
	private readonly ILabelTextService _labelText;
	private readonly LabelSubscriptionTracker _subscriptions;
	private readonly IWidgetStateService _widgetState;
	private readonly WidgetStateSubscriptionTracker _widgetStateSubscriptions;
	private readonly VariableInterestTracker _variableInterest;
	private readonly VariableBroadcaster _variableBroadcaster;

	private readonly IUiTransportMessageHandler<GetMusicPlayerInstancesRequest, GetMusicPlayerInstancesResponse>
		_getMusicPlayerInstances;

	private readonly IUiTransportMessageHandler<GetMusicPlayerStateRequest, GetMusicPlayerStateResponse>
		_getMusicPlayerState;

	private readonly IUiTransportMessageHandler<GetWeatherInstancesRequest, GetWeatherInstancesResponse>
		_getWeatherInstances;

	private readonly IUiTransportMessageHandler<GetWeatherStateRequest, GetWeatherStateResponse> _getWeatherState;

	private readonly IUiTransportMessageHandler<GetFolderViewsRequest, GetFolderViewsResponse> _getFolderViews;

	private readonly IUiTransportMessageHandler<GetVariableCatalogProvidersRequest, GetVariableCatalogProvidersResponse>
		_getVariableCatalogProviders;

	private readonly IUiTransportMessageHandler<DiscoverCatalogVariablesRequest, DiscoverCatalogVariablesResponse>
		_discoverCatalogVariables;

	private readonly IUiTransportMessageHandler<ResolveCatalogVariableRequest, ResolveCatalogVariableResponse>
		_resolveCatalogVariable;

	private readonly IUiTransportMessageHandler<ReportFolderChangedRequest, ReportFolderChangedResponse>
		_reportFolderChanged;

	private readonly IUiTransportMessageHandler<ListUiPreviewsRequest, ListUiPreviewsResponse> _listUiPreviews;

	private readonly LogStreamSubscriptionTracker _logSubscriptions;
	private readonly ILogFileReader _logFileReader;
	private readonly DeviceConnectionTracker _deviceConnections;

	// The client id this connection registered, distinct from _connectionId: client groups are keyed by
	// it, so an event attributed to a connection id reaches nobody. Written once from RegisterClient,
	// read from other dispatched messages, hence volatile.
	private volatile string? _registeredClientId;
	private readonly IMusicPlayerClientSync _musicPlayerClientSync;
	private readonly IUiSessionBroker _uiSessions;
	private readonly IConfigUiSessionOpener _configUiSessions;
	private readonly IWidgetUiSessionOpener _widgetUiSessions;
	private readonly IUiPreviewSessionOpener _uiPreviewSessions;
	private readonly IFolderUiSessionOpener _folderUiSessions;
	private readonly IModalUiSessionOpener _modalUiSessions;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly IUiTransport _transport;
	private readonly WebSocketUiTransport _webSocketTransport;
	private readonly SemaphoreSlim _dispatch = new(1, 1);

	public UiWebSocketDispatcher(
		string connectionId,
		ClaimsPrincipal principal,
		Action abort,
		ILabelTextService labelText,
		LabelSubscriptionTracker subscriptions,
		IWidgetStateService widgetState,
		WidgetStateSubscriptionTracker widgetStateSubscriptions,
		VariableInterestTracker variableInterest,
		VariableBroadcaster variableBroadcaster,
		IUiTransportMessageHandler<GetMusicPlayerInstancesRequest, GetMusicPlayerInstancesResponse>
			getMusicPlayerInstances,
		IUiTransportMessageHandler<GetMusicPlayerStateRequest, GetMusicPlayerStateResponse> getMusicPlayerState,
		IUiTransportMessageHandler<GetWeatherInstancesRequest, GetWeatherInstancesResponse> getWeatherInstances,
		IUiTransportMessageHandler<GetFolderViewsRequest, GetFolderViewsResponse> getFolderViews,
		IUiTransportMessageHandler<GetWeatherStateRequest, GetWeatherStateResponse> getWeatherState,
		IUiTransportMessageHandler<GetVariableCatalogProvidersRequest, GetVariableCatalogProvidersResponse>
			getVariableCatalogProviders,
		IUiTransportMessageHandler<DiscoverCatalogVariablesRequest, DiscoverCatalogVariablesResponse>
			discoverCatalogVariables,
		IUiTransportMessageHandler<ResolveCatalogVariableRequest, ResolveCatalogVariableResponse>
			resolveCatalogVariable,
		IUiTransportMessageHandler<ReportFolderChangedRequest, ReportFolderChangedResponse> reportFolderChanged,
		IUiTransportMessageHandler<ListUiPreviewsRequest, ListUiPreviewsResponse> listUiPreviews,
		LogStreamSubscriptionTracker logSubscriptions,
		ILogFileReader logFileReader,
		DeviceConnectionTracker deviceConnections,
		IMusicPlayerClientSync musicPlayerClientSync,
		IUiSessionBroker uiSessions,
		IConfigUiSessionOpener configUiSessions,
		IWidgetUiSessionOpener widgetUiSessions,
		IUiPreviewSessionOpener uiPreviewSessions,
		IFolderUiSessionOpener folderUiSessions,
		IModalUiSessionOpener modalUiSessions,
		IHostApplicationLifetime lifetime,
		IUiTransport transport,
		WebSocketUiTransport webSocketTransport,
		CancellationToken connectionCancellation)
	{
		_connectionId = connectionId;
		_principal = principal;
		_connectionCancellation = connectionCancellation;
		_abort = abort;
		_labelText = labelText;
		_subscriptions = subscriptions;
		_widgetState = widgetState;
		_widgetStateSubscriptions = widgetStateSubscriptions;
		_variableInterest = variableInterest;
		_variableBroadcaster = variableBroadcaster;
		_getMusicPlayerInstances = getMusicPlayerInstances;
		_getMusicPlayerState = getMusicPlayerState;
		_getWeatherInstances = getWeatherInstances;
		_getFolderViews = getFolderViews;
		_getWeatherState = getWeatherState;
		_getVariableCatalogProviders = getVariableCatalogProviders;
		_discoverCatalogVariables = discoverCatalogVariables;
		_resolveCatalogVariable = resolveCatalogVariable;
		_reportFolderChanged = reportFolderChanged;
		_listUiPreviews = listUiPreviews;
		_logSubscriptions = logSubscriptions;
		_logFileReader = logFileReader;
		_deviceConnections = deviceConnections;
		_musicPlayerClientSync = musicPlayerClientSync;
		_uiSessions = uiSessions;
		_configUiSessions = configUiSessions;
		_widgetUiSessions = widgetUiSessions;
		_uiPreviewSessions = uiPreviewSessions;
		_folderUiSessions = folderUiSessions;
		_modalUiSessions = modalUiSessions;
		_lifetime = lifetime;
		_transport = transport;
		_webSocketTransport = webSocketTransport;
	}

	public Task<bool> ConnectedAsync()
	{
		if (_lifetime.ApplicationStopping.IsCancellationRequested)
		{
			return Task.FromResult(false);
		}

		var deviceId = Guid.TryParse(_principal.FindFirst(AuthDefaults.DeviceClaim)?.Value, out var parsed)
			? parsed
			: (Guid?)null;
		if (deviceId is { } revoked && _deviceConnections.IsRevoked(revoked))
		{
			return Task.FromResult(false);
		}

		_deviceConnections.Attach(_connectionId, deviceId, _abort);
		return Task.FromResult(true);
	}

	public async Task ActivateAsync()
	{
		var deviceId = Guid.TryParse(_principal.FindFirst(AuthDefaults.DeviceClaim)?.Value, out var parsed)
			? parsed
			: (Guid?)null;
		await _transport.AddToGroup(_connectionId, VariableGroups.WatchAll, _connectionCancellation);
		if (deviceId is { } id)
		{
			await _transport.AddToGroup(_connectionId, UiDeviceGroups.For(id), _connectionCancellation);
		}

		if (IsAdmin)
		{
			await _transport.AddToGroup(_connectionId, UiAdminGroups.Admin, _connectionCancellation);
		}
	}

	public Task DisconnectedAsync()
	{
		_subscriptions.RemoveConnection(_connectionId);
		_widgetStateSubscriptions.RemoveConnection(_connectionId);
		_variableInterest.RemoveConnection(_connectionId);
		_logSubscriptions.Remove(_connectionId);
		_deviceConnections.Remove(_connectionId);
		_uiSessions.DetachConnection(_connectionId);
		return Task.CompletedTask;
	}

	public async Task<JsonElement?> DispatchAsync(string type,
		JsonElement? payload,
		CancellationToken cancellationToken)
	{
		await _dispatch.WaitAsync(cancellationToken);
		try
		{
			var result = type switch
			{
				"SubscribeLabel" => await SubscribeLabel(Arg<string>(payload, 0),
					Arg<string>(payload, 1),
					cancellationToken),
				"UnsubscribeLabel" => await UnsubscribeLabel(Arg<string>(payload, 0),
					Arg<string>(payload, 1),
					cancellationToken),
				"SubscribeWidgetState" => await SubscribeWidgetState(Arg<string>(payload, 0), cancellationToken),
				"UnsubscribeWidgetState" => await UnsubscribeWidgetState(Arg<string>(payload, 0), cancellationToken),
				"WatchVariables" => await WatchVariables(Arg<string[]>(payload, 0) ?? [], cancellationToken),
				"RenderLabelPreview" when IsAdmin => JsonSerializer.SerializeToElement<string?>(
					await _labelText.ResolvePreview(Arg<LabelImagePreviewRequest>(payload, 0), cancellationToken),
					UiWebSocketProtocol.Json),
				"GetMusicPlayerInstances" => await _getMusicPlayerInstances.Handle(new(), cancellationToken),
				"GetMusicPlayerState" => await _getMusicPlayerState.Handle(
					new() { InstanceId = Arg<string?>(payload, 0) },
					cancellationToken),
				"GetWeatherInstances" => await _getWeatherInstances.Handle(new(), cancellationToken),
				"GetFolderViews" => await _getFolderViews.Handle(new(), cancellationToken),
				"GetWeatherState" => await _getWeatherState.Handle(new() { InstanceId = Arg<string?>(payload, 0) },
					cancellationToken),
				"GetVariableCatalogProviders" => await _getVariableCatalogProviders.Handle(new(), cancellationToken),
				"DiscoverCatalogVariables" => await _discoverCatalogVariables.Handle(
					Arg<DiscoverCatalogVariablesRequest>(payload, 0),
					cancellationToken),
				"ResolveCatalogVariable" => await _resolveCatalogVariable.Handle(
					Arg<ResolveCatalogVariableRequest>(payload, 0),
					cancellationToken),
				"ReportFolderChanged" => await ReportFolderChanged(Arg<ReportFolderChangedRequest>(payload, 0),
					cancellationToken),
				"RegisterClient" => await RegisterClient(Arg<string>(payload, 0), cancellationToken),
				"SubscribeLogs" when IsAdmin => await SubscribeLogs(Arg<GetLogsRequest?>(payload, 0),
					Arg<string?>(payload, 1),
					cancellationToken),
				"UnsubscribeLogs" when IsAdmin => await UnsubscribeLogs(cancellationToken),
				"AttachUiSession" => _uiSessions.Attach(Arg<UiAttachSessionRequest>(payload, 0).SessionId,
					_connectionId,
					OwnerPrincipal()),
				"OpenConfigUiSession" => OpenConfigUiSession(Arg<OpenConfigUiSessionRequest>(payload, 0)),
				"OpenWidgetUiSession" => OpenWidgetUiSession(Arg<OpenWidgetUiSessionRequest>(payload, 0)),
				"ListUiPreviews" when IsAdmin => await _listUiPreviews.Handle(new(), cancellationToken),
				"OpenUiPreviewSession" when IsAdmin => OpenUiPreviewSession(
					Arg<OpenUiPreviewSessionRequest>(payload, 0)),
				"OpenFolderUiSession" => _folderUiSessions.Open(Arg<OpenFolderUiSessionRequest>(payload, 0),
					OwnerPrincipal()),
				"OpenModalUiSession" => _modalUiSessions.Open(Arg<OpenModalUiSessionRequest>(payload, 0),
					OwnerPrincipal()),
				"CompleteUiModal" => _modalUiSessions.Complete(Arg<CompleteUiModalRequest>(payload, 0),
					OwnerPrincipal()),
				"DetachUiSession" => DetachUiSession(Arg<string>(payload, 0)),
				"CloseUiSession" => _uiSessions.CloseOwned(Arg<string>(payload, 0),
					OwnerPrincipal(),
					"The client closed this view."),
				"SendUiEvent" => _uiSessions.SendEvent(Arg<UiSendEventRequest>(payload, 0),
					_connectionId,
					_registeredClientId),
				_ when IsKnown(type) => throw new UiWebSocketDispatchException("forbidden"),
				_ => throw new UiWebSocketDispatchException("unknown_type")
			};
			return result is null ? null : JsonSerializer.SerializeToElement(result, UiWebSocketProtocol.Json);
		}
		finally
		{
			_dispatch.Release();
		}
	}

	private async Task<LabelTextUpdatedEvent> SubscribeLabel(string widgetId,
		string state,
		CancellationToken cancellationToken)
	{
		state = LabelGroups.Normalize(state);
		_subscriptions.Add(_connectionId, widgetId, state);
		await _transport.AddToGroup(_connectionId, LabelGroups.For(widgetId, state), cancellationToken);
		var text = Guid.TryParse(widgetId, out var id)
			? await _labelText.ResolveText(id, state, cancellationToken)
			: null;
		return new LabelTextUpdatedEvent { WidgetId = widgetId, State = state, Text = text };
	}

	private async Task<object?> UnsubscribeLabel(string widgetId, string state, CancellationToken cancellationToken)
	{
		state = LabelGroups.Normalize(state);
		_subscriptions.Remove(_connectionId, widgetId, state);
		await _transport.RemoveFromGroup(_connectionId, LabelGroups.For(widgetId, state), cancellationToken);
		return null;
	}

	private async Task<WidgetStateUpdatedEvent> SubscribeWidgetState(string widgetId,
		CancellationToken cancellationToken)
	{
		_widgetStateSubscriptions.Add(_connectionId, widgetId);
		await _transport.AddToGroup(_connectionId, WidgetStateGroups.For(widgetId), cancellationToken);
		var resolution = Guid.TryParse(widgetId, out var id) ? await _widgetState.Resolve(id, cancellationToken) : null;
		return new WidgetStateUpdatedEvent
		{
			WidgetId = widgetId, StateId = resolution?.StateId ?? string.Empty,
			StateLabel = resolution?.StateLabel ?? default, States = resolution?.States.ToList() ?? []
		};
	}

	private async Task<object?> UnsubscribeWidgetState(string widgetId, CancellationToken cancellationToken)
	{
		_widgetStateSubscriptions.Remove(_connectionId, widgetId);
		await _transport.RemoveFromGroup(_connectionId, WidgetStateGroups.For(widgetId), cancellationToken);
		return null;
	}

	private async Task<VariablesChangedEvent> WatchVariables(string[] names, CancellationToken cancellationToken)
	{
		_variableInterest.Set(_connectionId, names);
		await _transport.RemoveFromGroup(_connectionId, VariableGroups.WatchAll, cancellationToken);
		return _variableBroadcaster.Snapshot(names);
	}

	private async Task<ReportFolderChangedResponse> ReportFolderChanged(ReportFolderChangedRequest request,
		CancellationToken cancellationToken)
	{
		request.DeviceId = Guid.TryParse(_principal.FindFirst(AuthDefaults.DeviceClaim)?.Value, out var id) ? id : null;
		return await _reportFolderChanged.Handle(request, cancellationToken);
	}

	private async Task<object?> RegisterClient(string clientId, CancellationToken cancellationToken)
	{
		_registeredClientId = clientId;
		_deviceConnections.Register(_connectionId, clientId);
		await _transport.AddToGroup(_connectionId, UiClientGroups.For(clientId), cancellationToken);
		foreach (var (name, payload) in _musicPlayerClientSync.BuildHandshake())
		{
			await _webSocketTransport.SendNotification(_connectionId, name, payload, cancellationToken);
		}

		return null;
	}

	private async Task<object?> SubscribeLogs(GetLogsRequest? filter,
		string? tailAnchor,
		CancellationToken cancellationToken)
	{
		var position = LogCursor.TryParse(tailAnchor, out var anchor) ? anchor : _logFileReader.CurrentEnd();
		_logSubscriptions.Set(_connectionId, filter?.ToQuery() ?? LogQuery.All, position);
		await _transport.AddToGroup(_connectionId, LogStreamGroups.For(_connectionId), cancellationToken);
		return null;
	}

	private async Task<object?> UnsubscribeLogs(CancellationToken cancellationToken)
	{
		_logSubscriptions.Remove(_connectionId);
		await _transport.RemoveFromGroup(_connectionId, LogStreamGroups.For(_connectionId), cancellationToken);
		return null;
	}

	private OpenConfigUiSessionResponse OpenConfigUiSession(OpenConfigUiSessionRequest request)
	{
		var ticket = _configUiSessions.Open(request, OwnerPrincipal());
		return new OpenConfigUiSessionResponse
			{ Accepted = ticket.Accepted, SessionId = ticket.SessionId, Code = ticket.Code, Message = ticket.Message };
	}

	private OpenWidgetUiSessionResponse OpenWidgetUiSession(OpenWidgetUiSessionRequest request)
	{
		var ticket = _widgetUiSessions.Open(request, OwnerPrincipal(), IsAdmin);
		return new OpenWidgetUiSessionResponse
			{ Accepted = ticket.Accepted, SessionId = ticket.SessionId, Code = ticket.Code, Message = ticket.Message };
	}

	private OpenUiPreviewSessionResponse OpenUiPreviewSession(OpenUiPreviewSessionRequest request)
	{
		var ticket = _uiPreviewSessions.Open(request, OwnerPrincipal(), IsAdmin);
		return new OpenUiPreviewSessionResponse
			{ Accepted = ticket.Accepted, SessionId = ticket.SessionId, Code = ticket.Code, Message = ticket.Message };
	}

	private object? DetachUiSession(string sessionId)
	{
		_uiSessions.Detach(sessionId, _connectionId);
		return null;
	}

	private bool IsAdmin => _principal.HasClaim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope);
	private string OwnerPrincipal() => _principal.FindFirst(AuthDefaults.DeviceClaim)?.Value ?? string.Empty;

	private static bool IsKnown(string type) => type is "RenderLabelPreview"
		or "SubscribeLogs"
		or "UnsubscribeLogs"
		or "ListUiPreviews"
		or "OpenUiPreviewSession";

	private static T Arg<T>(JsonElement? payload, int index)
	{
		if (payload is not { } value)
		{
			return default!;
		}

		var item = value.ValueKind == JsonValueKind.Array ? value[index] : value;
		return item.Deserialize<T>(UiWebSocketProtocol.Json)!;
	}

	public void Dispose() => _dispatch.Dispose();
}
