using System.Security.Claims;
using System.Text.Json;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Tests.UnitTests.Companion;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamUiDispatchTests : UiSessionFixture
{
	private const string Front = VideoStreamWorld.BuiltIn + "::front";

	private VideoStreamWorld _world = null!;
	private ScriptedVideoProvider _provider = null!;
	private TaskCompletionSource<VideoStreamSessionDescription> _hungOpen = null!;

	[SetUp]
	public async Task SetUpWorld()
	{
		_world = new VideoStreamWorld(inProcessTimeout: TimeSpan.FromMinutes(5));
		_provider = new ScriptedVideoProvider("front", "main", "hung");
		_hungOpen = new TaskCompletionSource<VideoStreamSessionDescription>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_provider.OnOpen = (request, _) => request.StreamId == "hung"
			? _hungOpen.Task
			: Task.FromResult(new VideoStreamSessionDescription("hls", "https://camera.local/" + request.StreamId));
		await _world.Context().RegisterProviderAsync(_provider);
	}

	[TearDown]
	public void TearDownWorld()
	{
		_hungOpen.TrySetResult(new VideoStreamSessionDescription("hls", "https://camera.local/late"));
		_world.Dispose();
	}

	[Test]
	public async Task An_open_is_answered_at_once_while_the_provider_hangs_and_the_connection_keeps_answering()
	{
		using var dispatcher = DispatcherFor("ui-1");

		var open = await OpenAsync(dispatcher, "hung");
		var catalog = await dispatcher.DispatchAsync("GetVideoStreams", null, CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(10));
		var providers = catalog!.Value.Deserialize<GetVideoStreamsResponse>(UiWebSocketProtocol.Json)!.Providers;

		Assert.Multiple(() =>
		{
			Assert.That(open.SessionId, Is.Not.Empty);
			Assert.That(open.Revision, Is.Zero);
			Assert.That(open.State, Is.EqualTo("opening"));
			Assert.That(_hungOpen.Task.IsCompleted, Is.False, "The provider answered, so nothing was proven.");
			Assert.That(providers.Single().Id, Is.EqualTo(Front));
			Assert.That(providers.Single().Streams.Select(stream => stream.Id), Is.EqualTo(new[] { "main", "hung" }));
			Assert.That(providers.Single().Streams[0].State, Is.EqualTo("connected"));
		});
	}

	[Test]
	public async Task Another_connection_cannot_keep_alive_or_close_a_session_it_does_not_own()
	{
		using var owner = DispatcherFor("ui-1");
		using var stranger = DispatcherFor("ui-2");
		var open = await OpenAsync(owner, "main");
		await _world.WaitForActiveAsync(open.SessionId);

		var refused = Assert.ThrowsAsync<UiWebSocketDispatchException>(() => DispatchAsync(stranger,
			"KeepAliveVideoStream",
			new KeepAliveVideoStreamRequest { SessionId = open.SessionId }));
		await DispatchAsync(stranger, "CloseVideoStream", new CloseVideoStreamRequest { SessionId = open.SessionId });
		await DispatchAsync(owner, "KeepAliveVideoStream", new KeepAliveVideoStreamRequest { SessionId = open.SessionId });

		Assert.Multiple(() =>
		{
			Assert.That(refused!.Code, Is.EqualTo("unknown_session"));
			Assert.That(_provider.Closes, Is.Empty);
			Assert.That(_world.Publisher.Of<VideoStreamSessionClosedNotification>(), Is.Empty);
		});
	}

	[Test]
	public void A_refusal_carries_its_code_and_a_localized_message()
	{
		using var dispatcher = DispatcherFor("ui-1");

		var refused = Assert.ThrowsAsync<UiWebSocketDispatchException>(() => DispatchAsync(dispatcher,
			"OpenVideoStream",
			new OpenVideoStreamRequest
			{
				ProviderId = VideoStreamWorld.BuiltIn + "::missing", StreamId = "main", AcceptedTransports = ["hls"]
			}));

		Assert.Multiple(() =>
		{
			Assert.That(refused!.Code, Is.EqualTo("unknown_provider"));
			Assert.That(TestLocalization.Resolver.Resolve(refused.LocalizedMessage!.Value, "en"),
				Is.EqualTo("This video source is not available."));
			Assert.That(TestLocalization.Resolver.Resolve(refused.LocalizedMessage!.Value, "de"),
				Is.EqualTo("Diese Videoquelle ist nicht verfügbar."));
		});
	}

	[Test]
	public async Task A_signal_request_without_a_signal_is_refused_as_malformed_not_as_too_large()
	{
		using var dispatcher = DispatcherFor("ui-1");
		var open = await OpenAsync(dispatcher, "main");
		await _world.WaitForActiveAsync(open.SessionId);

		var refused = Assert.ThrowsAsync<UiWebSocketDispatchException>(() => DispatchAsync(dispatcher,
			"SignalVideoStream",
			new SignalVideoStreamRequest { SessionId = open.SessionId }));

		Assert.Multiple(() =>
		{
			Assert.That(refused!.Code, Is.EqualTo("failed"));
			Assert.That(_provider.Signals, Is.Empty);
		});
	}

	[Test]
	public void Every_refusal_has_its_own_text_in_every_shipped_language()
	{
		string[] cultures = ["en", "de", "it", "cs", "pl", "es", "fr"];
		var errors = Enum.GetValues<VideoStreamError>();

		var english = errors.Select(error => Text(error, "en")).ToList();

		foreach (var culture in cultures)
		{
			var texts = errors.Select(error => Text(error, culture)).ToList();

			Assert.That(texts, Has.None.Null.And.None.StartsWith("Errors."), $"A refusal has no text in {culture}.");
			Assert.That(texts, Is.Unique, $"Two refusals read the same in {culture}.");
			if (culture != "en")
			{
				Assert.That(texts.Where((text, index) => text == english[index]), Is.Empty,
					$"{culture} falls back to English.");
			}
		}

		static string? Text(VideoStreamError error, string culture)
			=> TestLocalization.Resolver.Resolve(VideoStreamUiMapping.ErrorText(error), culture);
	}

	[Test]
	public async Task A_disconnect_closes_the_connections_sessions_as_consumer_disconnected()
	{
		var companion = new CompanionHarness();
		using var dispatcher = DisconnectableDispatcherFor("ui-1", companion);
		using var other = DispatcherFor("ui-2");
		var mine = await OpenAsync(dispatcher, "main");
		var theirs = await OpenAsync(other, "main");
		await _world.WaitForActiveAsync(mine.SessionId);
		await _world.WaitForActiveAsync(theirs.SessionId);

		await dispatcher.DisconnectedAsync();
		await VideoStreamWorld.WaitForAsync(() => !_provider.Closes.IsEmpty, "The provider was never told.");

		Assert.Multiple(() =>
		{
			Assert.That(_provider.Closes,
				Is.EqualTo(new[] { (mine.SessionId, VideoStreamSessionReason.ConsumerDisconnected) }));
			Assert.That(_world.Publisher.Of<VideoStreamSessionClosedNotification>()
				.Select(closed => closed.SessionId), Is.EqualTo(new[] { mine.SessionId }));
		});
	}

	private static async Task<OpenVideoStreamResponse> OpenAsync(UiWebSocketDispatcher dispatcher, string streamId)
	{
		var result = await DispatchAsync(dispatcher,
			"OpenVideoStream",
			new OpenVideoStreamRequest { ProviderId = Front, StreamId = streamId, AcceptedTransports = ["webrtc", "hls"] })
			.WaitAsync(TimeSpan.FromSeconds(10));
		return result!.Value.Deserialize<OpenVideoStreamResponse>(UiWebSocketProtocol.Json)!;
	}

	private static Task<JsonElement?> DispatchAsync(UiWebSocketDispatcher dispatcher, string type, object request)
		=> dispatcher.DispatchAsync(type,
			JsonSerializer.SerializeToElement(new[] { request }, UiWebSocketProtocol.Json),
			CancellationToken.None);

	private static ClaimsPrincipal Device()
		=> new(new ClaimsIdentity([new Claim(AuthDefaults.DeviceClaim, Guid.NewGuid().ToString())], "test"));

	private UiWebSocketDispatcher DispatcherFor(string connectionId)
		=> Build(connectionId, null);

	private UiWebSocketDispatcher DisconnectableDispatcherFor(string connectionId, CompanionHarness companion)
		=> Build(connectionId, companion);

	private UiWebSocketDispatcher Build(string connectionId, CompanionHarness? companion)
		=> new(connectionId: connectionId,
			principal: Device(),
			abort: static () => { },
			labelText: null!,
			subscriptions: new LabelSubscriptionTracker(),
			widgetState: null!,
			widgetStateSubscriptions: new WidgetStateSubscriptionTracker(),
			variableInterest: new VariableInterestTracker(),
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
			logSubscriptions: new LogStreamSubscriptionTracker(),
			logFileReader: null!,
			deviceConnections: new DeviceConnectionTracker(new RecordingEventBus(),
				Time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			musicPlayerClientSync: null!,
			uiSessions: Broker,
			configUiSessions: null!,
			widgetUiSessions: null!,
			uiPreviewSessions: null!,
			folderUiSessions: null!,
			getFolderViews: null!,
			getScreenSavers: null!,
			getDeviceScreenSaver: null!,
			screenSaverUiSessions: null!,
			modalUiSessions: null!,
			lifetime: null!,
			transport: null!,
			webSocketTransport: null!,
			companions: companion?.DeviceRegistry!,
			licenses: null!,
			accessTokenCutoff: new AccessTokenCutoff(),
			deviceSessionGuard: new DeviceSessionGuard(),
			videoStreams: _world.Broker,
			videoStreamProviders: _world.Registry,
			videoStreamConsumer: VideoStreamWorld.Consumer,
			connectionCancellation: CancellationToken.None);
}
