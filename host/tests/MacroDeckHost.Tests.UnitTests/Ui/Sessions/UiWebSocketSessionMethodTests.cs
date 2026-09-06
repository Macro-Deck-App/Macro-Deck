using System.Security.Claims;
using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// The transport dispatcher decides which principal a connection represents. The broker compares
/// only principal strings, so the admin-scope-does-not-bypass-owner-binding rule must be observed here.
/// </summary>
[TestFixture]
internal sealed class UiWebSocketSessionMethodTests : UiSessionFixture
{
	[Test]
	public async Task An_admin_scoped_connection_with_no_matching_device_claim_does_not_bypass_principal_binding()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, principal: DeviceA);
		using var dispatcher = DispatcherFor("c-admin", new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope));

		var response = await Attach(dispatcher, sessionId);
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.False, "An admin scope attached to another device's session.");
			Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(MessagesFor("c-admin"), Is.Empty);
		});
	}

	[Test]
	public async Task An_admin_scoped_connection_carrying_the_owning_device_claim_still_attaches()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, principal: DeviceA);
		using var dispatcher = DispatcherFor("c-owner",
			new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope),
			new Claim(AuthDefaults.DeviceClaim, DeviceA));

		var response = await Attach(dispatcher, sessionId);
		await WaitForMessagesAsync("c-owner", 1, "The owning device was never sent its tree.");

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(response.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c-owner"), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_nullable_operation_preserves_an_explicit_json_null_result()
	{
		using var dispatcher = DispatcherFor("c-admin",
			new NullLabelTextService(),
			new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope));
		var payload = JsonSerializer.SerializeToElement(new object[] { new LabelImagePreviewRequest() },
			UiWebSocketProtocol.Json);

		var response = await dispatcher.DispatchAsync("RenderLabelPreview", payload, CancellationToken.None);

		Assert.That(response, Is.Not.Null);
		Assert.That(response!.Value.ValueKind, Is.EqualTo(JsonValueKind.Null));
	}

	/// <summary>
	/// A request type the client calls but the dispatcher has no case for is answered <c>unknown_type</c>,
	/// which reaches the client as an empty result rather than as a failure - the folder view picker
	/// silently rendered an empty catalog until this route existed. Asserting the negative is the point:
	/// the handler and the client were both fine on their own, and only the wiring between them was not.
	/// </summary>
	[Test]
	public async Task The_folder_view_catalog_is_a_routed_request_type()
	{
		using var dispatcher = DispatcherFor("c-admin", new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope));

		var result = await dispatcher.DispatchAsync("GetFolderViews", null, CancellationToken.None);

		Assert.That(result, Is.Not.Null, "the folder view catalog is not routed to its handler");
	}

	/// <summary>The negative control for the test above: without it, a route that answered null and a name
	/// the dispatcher never heard of would be indistinguishable.</summary>
	[Test]
	public void An_unrouted_request_type_is_rejected_rather_than_answered()
	{
		using var dispatcher = DispatcherFor("c-admin", new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope));

		Assert.That(
			async () => await dispatcher.DispatchAsync("GetSomethingNobodyRoutes", null, CancellationToken.None),
			Throws.TypeOf<UiWebSocketDispatchException>()
				.With.Property(nameof(UiWebSocketDispatchException.Code)).EqualTo("unknown_type"));
	}

	/// <summary>
	/// A press dispatched through a UI session must reach the provider attributed to the client id the
	/// connection registered. It used to carry the connection id instead: every consumer addresses the
	/// origin by client id - client groups are keyed by it - so an action that answered its origin, an
	/// action modal among them, published to a group that could not exist and the reply was dropped with
	/// no error anywhere. The two id spaces never overlap, so asserting the value is not the connection
	/// id would pass on any string; the registered id is asserted directly.
	/// </summary>
	[Test]
	public async Task An_event_is_attributed_to_the_client_the_connection_registered()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared, DeviceA);
		using var dispatcher = ConnectedDispatcherFor("connection-1", new Claim(AuthDefaults.DeviceClaim, DeviceA));
		await Register(dispatcher, "client-1");
		var attached = await Attach(dispatcher, sessionId);
		Assert.That(attached.Accepted, Is.True, "The connection never attached, so no event could be relayed.");

		await SendEvent(dispatcher, sessionId);
		await WaitForAsync(() => provider.Events.Count >= 1, "The provider was never sent the event.");

		Assert.That(provider.Events[0].ClientId, Is.EqualTo("client-1"));
	}

	/// <summary>A connection that never registered a client has no origin to name, and the provider is
	/// told so rather than being handed the connection id as a stand-in.</summary>
	[Test]
	public async Task An_event_from_an_unregistered_connection_names_no_client()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared, DeviceA);
		using var dispatcher = ConnectedDispatcherFor("connection-2", new Claim(AuthDefaults.DeviceClaim, DeviceA));
		await Attach(dispatcher, sessionId);

		await SendEvent(dispatcher, sessionId);
		await WaitForAsync(() => provider.Events.Count >= 1, "The provider was never sent the event.");

		Assert.That(provider.Events[0].ClientId, Is.Null);
	}

	private static async Task Register(UiWebSocketDispatcher dispatcher, string clientId)
	{
		var payload = JsonSerializer.SerializeToElement(new object[] { clientId }, UiWebSocketProtocol.Json);
		await dispatcher.DispatchAsync("RegisterClient", payload, CancellationToken.None);
	}

	private static async Task SendEvent(UiWebSocketDispatcher dispatcher, string sessionId)
	{
		var payload = JsonSerializer.SerializeToElement(
			new object[] { new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = "press" } },
			UiWebSocketProtocol.Json);
		await dispatcher.DispatchAsync("SendUiEvent", payload, CancellationToken.None);
	}

	private static async Task<UiAttachSessionResponse> Attach(UiWebSocketDispatcher dispatcher, string sessionId)
	{
		var payload = JsonSerializer.SerializeToElement(
			new object[] { new UiAttachSessionRequest { SessionId = sessionId } },
			UiWebSocketProtocol.Json);
		var result = await dispatcher.DispatchAsync("AttachUiSession", payload, CancellationToken.None);
		return result!.Value.Deserialize<UiAttachSessionResponse>(UiWebSocketProtocol.Json)!;
	}

	private UiWebSocketDispatcher DispatcherFor(string connectionId, params Claim[] claims)
		=> DispatcherFor(connectionId, null, claims);

	/// <summary>A dispatcher wired with the collaborators <c>RegisterClient</c> actually touches, so a
	/// test can take a connection through the same registration a real client performs before it sends
	/// anything.</summary>
	private UiWebSocketDispatcher ConnectedDispatcherFor(string connectionId, params Claim[] claims)
		=> new(connectionId: connectionId,
			principal: new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
			abort: static () => { },
			labelText: null!,
			subscriptions: null!,
			widgetState: null!,
			widgetStateSubscriptions: null!,
			variableInterest: null!,
			variableBroadcaster: null!,
			getMusicPlayerInstances: null!,
			getMusicPlayerState: null!,
			getWeatherInstances: null!,
			getWeatherState: null!,
			getVariableCatalogProviders: null!,
			discoverCatalogVariables: null!,
			resolveCatalogVariable: null!,
			reportFolderChanged: null!,
			listUiPreviews: null!,
			logSubscriptions: null!,
			logFileReader: null!,
			deviceConnections: new DeviceConnectionTracker(new RecordingEventBus(), Time),
			musicPlayerClientSync: new NoMusicPlayerHandshake(),
			uiSessions: Broker,
			configUiSessions: null!,
			widgetUiSessions: null!,
			uiPreviewSessions: null!,
			folderUiSessions: null!,
			getFolderViews: new RecordingFolderViewsHandler(),
			modalUiSessions: null!,
			lifetime: null!,
			transport: Transport,
			webSocketTransport: null!,
			connectionCancellation: CancellationToken.None);

	private UiWebSocketDispatcher DispatcherFor(
		string connectionId,
		ILabelTextService? labelText,
		params Claim[] claims)
		=> new(connectionId: connectionId,
			principal: new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
			abort: static () => { },
			labelText: labelText!,
			subscriptions: null!,
			widgetState: null!,
			widgetStateSubscriptions: null!,
			variableInterest: null!,
			variableBroadcaster: null!,
			getMusicPlayerInstances: null!,
			getMusicPlayerState: null!,
			getWeatherInstances: null!,
			getWeatherState: null!,
			getVariableCatalogProviders: null!,
			discoverCatalogVariables: null!,
			resolveCatalogVariable: null!,
			reportFolderChanged: null!,
			listUiPreviews: null!,
			logSubscriptions: null!,
			logFileReader: null!,
			deviceConnections: null!,
			musicPlayerClientSync: null!,
			uiSessions: Broker,
			configUiSessions: null!,
			widgetUiSessions: null!,
			uiPreviewSessions: null!,
			folderUiSessions: null!,
			getFolderViews: new RecordingFolderViewsHandler(),
			modalUiSessions: null!,
			lifetime: null!,
			transport: null!,
			webSocketTransport: null!,
			connectionCancellation: CancellationToken.None);

	/// <summary>Stands in for the real catalog handler: this test asserts the route exists, not what the
	/// catalog holds.</summary>
	private sealed class RecordingFolderViewsHandler
		: IUiTransportMessageHandler<GetFolderViewsRequest, GetFolderViewsResponse>
	{
		public ValueTask<GetFolderViewsResponse> Handle(
			GetFolderViewsRequest request,
			CancellationToken cancellationToken)
			=> ValueTask.FromResult(new GetFolderViewsResponse());
	}

	/// <summary>Keeps the handshake empty so registration needs no WebSocket transport to write to.</summary>
	private sealed class NoMusicPlayerHandshake : IMusicPlayerClientSync
	{
		public IReadOnlyList<(string Name, object Payload)> BuildHandshake() => [];
	}

	private sealed class NullLabelTextService : ILabelTextService
	{
		public Task<string?> ResolveText(Guid widgetId, string state, CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(null);

		public Task<string?> ResolvePreview(
			LabelImagePreviewRequest request,
			CancellationToken cancellationToken = default)
			=> Task.FromResult<string?>(null);
	}
}
