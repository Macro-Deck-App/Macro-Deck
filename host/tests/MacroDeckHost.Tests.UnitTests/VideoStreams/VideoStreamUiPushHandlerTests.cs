using MacroDeck.Localization;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;
using MacroDeckHost.Application.VideoStreams;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

[TestFixture]
internal sealed class VideoStreamUiPushHandlerTests
{
	[Test]
	public async Task Session_pushes_reach_only_the_owning_connection()
	{
		var transport = new RecordingUiTransport();
		var handler = new VideoStreamUiPushHandler(transport);

		await handler.Handle(new VideoStreamSessionChangedNotification("ui-1",
				"s-1",
				3,
				VideoStreamSessionState.Reconnecting,
				new VideoStreamSessionDescription("hls", "https://camera.local/live"),
				VideoStreamSessionReason.ProviderReconnecting,
				LocalizedText.FromLiteral("OBS is reconnecting")),
			CancellationToken.None);
		await handler.Handle(new VideoStreamSignalNotification("ui-1", "s-1", new VideoStreamSignal("answer", "sdp")),
			CancellationToken.None);
		await handler.Handle(new VideoStreamSessionClosedNotification("ui-1",
				"s-1",
				VideoStreamSessionReason.ProviderRemoved,
				null,
				null),
			CancellationToken.None);

		var changed = transport.ConnectionMessages.Select(sent => sent.Message).OfType<VideoStreamSessionChangedEvent>().Single();
		var signal = transport.ConnectionMessages.Select(sent => sent.Message).OfType<VideoStreamSignalEvent>().Single();
		var closed = transport.ConnectionMessages.Select(sent => sent.Message).OfType<VideoStreamSessionClosedEvent>().Single();
		Assert.Multiple(() =>
		{
			Assert.That(transport.ConnectionMessages.Select(sent => sent.ConnectionId), Is.All.EqualTo("ui-1"));
			Assert.That(transport.Broadcasts, Is.Empty);
			Assert.That(changed.Revision, Is.EqualTo(3));
			Assert.That(changed.State, Is.EqualTo("reconnecting"));
			Assert.That(changed.Reason, Is.EqualTo("provider_reconnecting"));
			Assert.That(changed.Description!.Url, Is.EqualTo("https://camera.local/live"));
			Assert.That(changed.Message!.Value.Literal, Is.EqualTo("OBS is reconnecting"));
			Assert.That(signal.Signal.Type, Is.EqualTo("answer"));
			Assert.That(closed.Reason, Is.EqualTo("provider_removed"));
			Assert.That(closed.Error, Is.Null);
			Assert.That(closed.Message, Is.Null);
		});
	}

	[Test]
	public async Task A_failed_close_names_the_error_and_explains_it_when_the_provider_said_nothing()
	{
		var transport = new RecordingUiTransport();

		await new VideoStreamUiPushHandler(transport).Handle(new VideoStreamSessionClosedNotification("ui-1",
				"s-1",
				VideoStreamSessionReason.Failed,
				null,
				VideoStreamError.TransportNotAccepted),
			CancellationToken.None);

		var closed = (VideoStreamSessionClosedEvent)transport.ConnectionMessages.Single().Message;
		Assert.Multiple(() =>
		{
			Assert.That(closed.Reason, Is.EqualTo("failed"));
			Assert.That(closed.Error, Is.EqualTo("transport_not_accepted"));
			Assert.That(TestLocalization.Resolver.Resolve(closed.Message!.Value, "en"),
				Is.EqualTo("This video stream cannot be played on this device."));
		});
	}

	[Test]
	public async Task A_catalog_change_reaches_every_client()
	{
		var transport = new RecordingUiTransport();

		await new VideoStreamUiPushHandler(transport).Handle(new VideoStreamCatalogChangedNotification(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(transport.Broadcasts.Single(), Is.TypeOf<VideoStreamCatalogChangedEvent>());
			Assert.That(transport.ConnectionMessages, Is.Empty);
		});
	}
}
