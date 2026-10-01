using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SdkVideoStreamHandler =
	MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider.VideoStreamProviderCapabilityHandler;
using SdkVideoStreamRegistry = MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider.VideoStreamProviderRegistry;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class VideoStreamParityContractTests : CapabilityContractFixture
{
	private const string Front = PluginId + "::front";
	private const string Back = PluginId + "::back";

	private static readonly string[] _expected =
	[
		"catalog back[main] front[main,sub]",
		"front opened main accepting hls,mjpeg",
		"rev1 Active None hls /front/main",
		"rev2 Reconnecting SourceLost hls /front/main",
		"front suspended",
		"rev3 Suspended None hls /front/main",
		"front resumed",
		"rev4 Active None mjpeg /front/resumed",
		"token stable",
		"front closed ConsumerClosed",
		"closed ConsumerClosed",
		"token revoked",
		"back opened main accepting hls,mjpeg",
		"back closed ProviderRemoved",
		"closed ProviderRemoved",
		"catalog front[main,sub]",
		"catalog back[main] front[main,sub]",
		"rev1 Active None hls /back/main",
		"closed ProviderRemoved",
		"catalog ",
		"provider url never published"
	];

	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.VideoStreamProvider,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static async Task<IReadOnlyList<string>> WalkAsync(
		VideoStreamProviderRegistry providers,
		VideoStreamSessionBroker broker,
		VideoStreamRelay relay,
		ConcurrentPublisher published,
		IVideoStreamProviderContext context,
		Cameras cameras,
		Func<Task> endProviderSession)
	{
		var log = new List<string>();

		await context.RegisterProviderAsync(cameras.Front);
		await context.RegisterProviderAsync(cameras.Back);
		await WaitForAsync(() => Catalog(providers) == "back[main] front[main,sub]", "both providers never listed");
		log.Add("catalog " + Catalog(providers));

		var first = broker.OpenSession("ui-1", Front, "main", ["hls", "mjpeg"]).SessionId;
		await WaitForAsync(() => published.Changes(first).Count == 1, "the session never became active");
		log.Add(cameras.Log.Single());
		log.Add(Describe(published.Changes(first)[0]));

		await context.UpdateSessionAsync(first, VideoStreamSessionState.Reconnecting,
			reason: VideoStreamSessionReason.SourceLost);
		await WaitForAsync(() => published.Changes(first).Count == 2, "the provider's update never arrived");
		log.Add(Describe(published.Changes(first)[1]));

		broker.SuspendSession("ui-1", first);
		await WaitForAsync(() => published.Changes(first).Count == 3, "the suspend never completed");
		log.Add(cameras.Log.Last());
		log.Add(Describe(published.Changes(first)[2]));

		broker.ResumeSession("ui-1", first);
		await WaitForAsync(() => published.Changes(first).Count == 4, "the resume never completed");
		log.Add(cameras.Log.Last());
		log.Add(Describe(published.Changes(first)[3]));

		var tokens = published.Changes(first).Select(changed => TokenOf(changed.Description!.Url)).Distinct().ToList();
		log.Add(tokens.Count == 1 ? "token stable" : "token changed: " + string.Join(",", tokens));

		broker.CloseSession("ui-1", first);
		await WaitForAsync(() => cameras.Log.Count == 4 && published.Closed(first) is not null, "the close never completed");
		log.Add(cameras.Log.Last());
		log.Add("closed " + published.Closed(first)!.Reason);
		log.Add(relay.Acquire(tokens[0], out _) == VideoStreamRelayAcquisition.NotFound ? "token revoked" : "token alive");

		var second = broker.OpenSession("ui-1", Back, "main", ["hls", "mjpeg"]).SessionId;
		await WaitForAsync(() => published.Changes(second).Count == 1, "the second session never became active");
		log.Add(cameras.Log.Last());

		await context.UnregisterProviderAsync("back");
		await WaitForAsync(() => published.Closed(second) is not null && Catalog(providers) == "front[main,sub]",
			"unregistering never closed the session");
		log.Add(cameras.Log.Last());
		log.Add("closed " + published.Closed(second)!.Reason);
		log.Add("catalog " + Catalog(providers));

		await context.RegisterProviderAsync(cameras.Back);
		await WaitForAsync(() => Catalog(providers) == "back[main] front[main,sub]", "re-registering never listed");
		log.Add("catalog " + Catalog(providers));

		var third = broker.OpenSession("ui-1", Back, "main", ["hls"]).SessionId;
		await WaitForAsync(() => published.Changes(third).Count == 1, "the re-registered provider never opened");
		log.Add(Describe(published.Changes(third)[0]));

		await endProviderSession();
		await WaitForAsync(() => published.Closed(third) is not null && Catalog(providers).Length == 0,
			"ending the provider never closed its session");
		log.Add("closed " + published.Closed(third)!.Reason);
		log.Add("catalog " + Catalog(providers));
		log.Add(published.AnyUrlContains("camera.local") ? "provider url leaked" : "provider url never published");

		return log;
	}

	[Test]
	public async Task A_built_in_camera_integration_walks_the_contract()
	{
		var published = new ConcurrentPublisher();
		using var providers = new VideoStreamProviderRegistry(IntegrationRegistry,
			SessionRegistry,
			Invoker,
			published,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		var relay = new VideoStreamRelay();
		using var broker = new VideoStreamSessionBroker(providers,
			SessionRegistry,
			published,
			relay,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		var cameras = new Cameras();

		var log = await WalkAsync(providers,
			broker,
			relay,
			published,
			new IntegrationVideoStreamProviderContext(PluginId, providers, broker),
			cameras,
			() => providers.WithdrawInProcessAsync(PluginId));

		Assert.That(log, Is.EqualTo(_expected));
	}

	[Test]
	public async Task A_camera_plugin_across_the_wire_walks_the_same_contract_to_the_same_log()
	{
		var published = new ConcurrentPublisher();
		using var providers = new VideoStreamProviderRegistry(IntegrationRegistry,
			SessionRegistry,
			Invoker,
			published,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		var relay = new VideoStreamRelay();
		using var broker = new VideoStreamSessionBroker(providers,
			SessionRegistry,
			published,
			relay,
			TimeProvider.System,
			Serilog.Core.Logger.None);
		var pluginSessions = new VideoStreamPluginSessions(SessionRegistry, providers, broker);
		await pluginSessions.StartAsync(CancellationToken.None);
		try
		{
			var cameras = new Cameras();
			using var pluginRegistry = new SdkVideoStreamRegistry(CreatePluginHostInvoker(),
				PluginConnection,
				TestMetadata.Default,
				TimeProvider.System,
				Serilog.Core.Logger.None);
			var router = CreateRouter(providers, broker);
			HostInvokeHandler = (correlationId, payload, cancellationToken)
				=> router.RouteAsync(PluginId, SessionId, Link, correlationId, payload, cancellationToken);

			await ConnectAsync([new SdkVideoStreamHandler([cameras], pluginRegistry, Serilog.Core.Logger.None)],
				[Provider()],
				[CapabilityKinds.VideoStreamProvider]);
			await providers.AttachRemoteAsync(PluginId, SessionId, Link);

			var log = await WalkAsync(providers,
				broker,
				relay,
				published,
				pluginRegistry.ContextFor(cameras),
				cameras,
				() =>
				{
					Disconnect();
					return Task.CompletedTask;
				});

			Assert.That(log, Is.EqualTo(_expected));
		}
		finally
		{
			await pluginSessions.StopAsync(CancellationToken.None);
		}
	}

	private PluginCallbackRouter CreateRouter(VideoStreamProviderRegistry providers, VideoStreamSessionBroker broker)
	{
		var services = new ServiceCollection().BuildServiceProvider();
		return new PluginCallbackRouter(SessionRegistry,
			Invoker,
			services.GetRequiredService<IServiceScopeFactory>(),
			Notifications,
			new CallbackFakeDeckNavigator(),
			new CallbackFakeScriptApi(),
			new CallbackFakeWidgetApi(),
			new CallbackFakeWidgetIconInvalidator(),
			new CallbackFakeUserVariableApi(),
			new CallbackFakeActionInteractions(),
			new RecordingUiSessionSink(),
			DeviceRegistry,
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			new NullUiTransport(),
			new HostCallbackThrottle(TimeProvider.System, capacity: 1000, refillPerSecond: 1000),
			new CallbackFakeHostLockState(),
			Serilog.Core.Logger.None,
			videoStreamProviders: providers,
			videoStreamSessions: broker);
	}

	private static string Catalog(VideoStreamProviderRegistry providers)
		=> string.Join(" ",
			providers.GetProviders()
				.Select(entry => $"{entry.ProviderId}[{string.Join(",", entry.Streams.Select(stream => stream.Id))}]"));

	private static string Describe(VideoStreamSessionChangedNotification changed)
		=> $"rev{changed.Revision} {changed.State} {changed.Reason} {changed.Description?.Transport} " +
			changed.Description?.Url[(VideoStreamRelay.PathPrefix.Length + TokenOf(changed.Description.Url).Length)..];

	private static string TokenOf(string relayUrl)
		=> relayUrl[VideoStreamRelay.PathPrefix.Length..].Split('/')[0];

	private sealed class ConcurrentPublisher : IPublisher
	{
		private readonly ConcurrentQueue<object> _published = new();

		public IReadOnlyList<VideoStreamSessionChangedNotification> Changes(string sessionId)
			=> [.. _published.OfType<VideoStreamSessionChangedNotification>().Where(changed => changed.SessionId == sessionId)];

		public bool AnyUrlContains(string text)
			=> _published.OfType<VideoStreamSessionChangedNotification>()
				.Any(changed => changed.Description?.Url.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

		public VideoStreamSessionClosedNotification? Closed(string sessionId)
			=> _published.OfType<VideoStreamSessionClosedNotification>().SingleOrDefault(closed => closed.SessionId == sessionId);

		public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
			where TNotification : INotification
		{
			_published.Enqueue(notification!);
			return ValueTask.CompletedTask;
		}

		public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
		{
			_published.Enqueue(notification);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class Cameras : IPluginIntegration, IVideoStreamIntegration
	{
		public Cameras()
		{
			Front = new Camera("front", ["main", "sub"], Log);
			Back = new Camera("back", ["main"], Log);
		}

		public ConcurrentQueue<string> Log { get; } = new();

		public Camera Front { get; }

		public Camera Back { get; }

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class Camera(string id, IReadOnlyList<string> streams, ConcurrentQueue<string> log)
		: IVideoStreamProvider
	{
		public string Id { get; } = id;

		public LocalizedText Name => LocalizedText.FromLiteral("Camera " + Id);

		public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<VideoStreamDescriptor>>(
			[
				.. streams.Select(stream => new VideoStreamDescriptor(stream,
					LocalizedText.FromLiteral(stream),
					Width: 1920,
					Height: 1080))
			]);

		public Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request,
			CancellationToken cancellationToken)
		{
			log.Enqueue($"{Id} opened {request.StreamId} accepting {string.Join(",", request.AcceptedTransports)}");
			return Task.FromResult(VideoStreamSessionDescription.Hls($"https://camera.local/{Id}/{request.StreamId}"));
		}

		public Task SuspendAsync(string sessionId, CancellationToken cancellationToken)
		{
			log.Enqueue($"{Id} suspended");
			return Task.CompletedTask;
		}

		public Task<VideoStreamSessionDescription?> ResumeAsync(string sessionId, CancellationToken cancellationToken)
		{
			log.Enqueue($"{Id} resumed");
			return Task.FromResult<VideoStreamSessionDescription?>(
				VideoStreamSessionDescription.Mjpeg($"https://camera.local/{Id}/resumed"));
		}

		public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
		{
			log.Enqueue($"{Id} closed {reason}");
			return Task.CompletedTask;
		}
	}
}
