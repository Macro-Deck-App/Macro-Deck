using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A31 - <see cref="FakeVideoStreamProviderContext" /> applies the registration rules the SDK and
/// the host apply, and <see cref="VideoStreamProviderTestClient" /> drives a plugin's providers the way the
/// host does.
/// </summary>
[TestFixture]
public class A31_VideoStreamProviderFakeTests
{
	[Test]
	public async Task Registering_a_provider_id_twice_is_rejected()
	{
		var context = new FakeVideoStreamProviderContext();
		await context.RegisterProviderAsync(new Camera("cam"));

		Assert.That(() => context.RegisterProviderAsync(new Camera("cam")), Throws.ArgumentException);
		Assert.That(context.Providers, Has.Count.EqualTo(1));
	}

	[Test]
	public void An_id_that_is_not_a_resource_local_id_is_rejected()
		=> Assert.That(() => new FakeVideoStreamProviderContext().RegisterProviderAsync(new Camera("front door")),
			Throws.ArgumentException);

	[Test]
	public async Task A_seventeenth_provider_is_rejected()
	{
		var context = new FakeVideoStreamProviderContext();
		for (var index = 0; index < 16; index++)
		{
			await context.RegisterProviderAsync(new Camera($"cam-{index}"));
		}

		Assert.That(() => context.RegisterProviderAsync(new Camera("cam-16")), Throws.ArgumentException);
	}

	[Test]
	public async Task A_registration_names_the_provider_qualified_by_the_plugin()
	{
		var registration = await new FakeVideoStreamProviderContext().RegisterProviderAsync(new Camera("cam"));

		Assert.That(registration,
			Is.EqualTo(new VideoStreamProviderRegistration($"{FakeVideoStreamProviderContext.PluginId}::cam", "cam")));
	}

	[Test]
	public async Task Unregistering_an_unknown_id_is_a_silent_no_op()
	{
		var context = new FakeVideoStreamProviderContext();
		await context.RegisterProviderAsync(new Camera("cam"));

		Assert.DoesNotThrowAsync(() => context.UnregisterProviderAsync("never-registered"));
		Assert.That(context.Providers, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Every_call_is_recorded_in_order()
	{
		var context = new FakeVideoStreamProviderContext();

		await context.RegisterProviderAsync(new Camera("cam"));
		await context.NotifyStreamsChangedAsync("cam");
		await context.UpdateSessionAsync("s1", VideoStreamSessionState.Reconnecting, reason: VideoStreamSessionReason.SourceLost);
		await context.CloseSessionAsync("s1");
		await context.UnregisterProviderAsync("cam");

		Assert.Multiple(() =>
		{
			Assert.That(context.Calls.Select(call => call.Kind),
				Is.EqualTo(new[]
				{
					VideoStreamProviderCallKind.Register, VideoStreamProviderCallKind.StreamsChanged,
					VideoStreamProviderCallKind.SessionUpdate, VideoStreamProviderCallKind.SessionClose,
					VideoStreamProviderCallKind.Unregister
				}));
			Assert.That(context.Calls[2].Reason, Is.EqualTo(VideoStreamSessionReason.SourceLost));
			Assert.That(context.Calls[3].Reason, Is.EqualTo(VideoStreamSessionReason.ProviderClosed));
			Assert.That(context.Providers, Is.Empty);
		});
	}

	[Test]
	public async Task Over_the_wire_a_registered_provider_is_listed_opened_and_closed_once()
	{
		var camera = new Camera("cam");
		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.RegisterIntegration(_ => new CameraIntegration(camera));
		await using var plugin = await host.HostAsync(builder);
		var session = await host.WaitForSessionAsync();

		VideoStreamProviderDescribePayload? described = null;
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (described?.Providers.Count != 1 && DateTime.UtcNow < deadline)
		{
			await Task.Delay(20);
			described = (await session.VideoStreamProvider.DescribeAsync()).DataAs<VideoStreamProviderDescribePayload>();
		}
		var streams = (await session.VideoStreamProvider.GetStreamsAsync("cam"))
			.DataAs<VideoStreamProviderStreamsResult>();
		var opened = (await session.VideoStreamProvider.OpenSessionAsync("s1", "cam", "main", ["mjpeg", "hls"]))
			.DataAs<VideoStreamSessionOpenResult>();
		var closed = await session.VideoStreamProvider.CloseSessionAsync("s1", "cam");
		await session.VideoStreamProvider.CloseSessionAsync("s1", "cam");

		Assert.Multiple(() =>
		{
			Assert.That(streams!.Streams.Single().Id, Is.EqualTo("main"));
			Assert.That(opened!.Description.Transport, Is.EqualTo("hls"));
			Assert.That(opened.Description.Url, Is.EqualTo("http://camera.local/main.m3u8"));
			Assert.That(opened.RegistrationId, Is.EqualTo(described!.Providers[0].RegistrationId));
			Assert.That(closed.Succeeded, Is.True);
			Assert.That(camera.Closes, Is.EqualTo(new[] { ("s1", VideoStreamSessionReason.ConsumerClosed) }));
		});
	}

	private sealed class Camera(string id) : IVideoStreamProvider
	{
		private readonly ConcurrentQueue<(string, VideoStreamSessionReason)> _closes = new();

		public string Id { get; } = id;

		public LocalizedText Name { get; } = LocalizedText.FromLiteral("Camera");

		public IReadOnlyList<(string, VideoStreamSessionReason)> Closes => [.. _closes];

		public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
			=> Task.FromResult<IReadOnlyList<VideoStreamDescriptor>>(
				[new VideoStreamDescriptor("main", LocalizedText.FromLiteral("Main"))]);

		public Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));

		public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
		{
			_closes.Enqueue((sessionId, reason));
			return Task.CompletedTask;
		}
	}

	private sealed class CameraIntegration(Camera camera) : IPluginIntegration, IVideoStreamIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default)
			=> context.RegisterProviderAsync(camera, cancellationToken);
	}
}
