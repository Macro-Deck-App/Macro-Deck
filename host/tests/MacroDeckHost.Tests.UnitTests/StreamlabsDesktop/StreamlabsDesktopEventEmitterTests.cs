using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopEventEmitterTests
{
	private RecordingPublisher _publisher = null!;
	private StreamlabsDesktopEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new StreamlabsDesktopEventEmitter(_publisher);
		_emitter.MarkReady();
	}

	[Test]
	public void NothingIsPublishedBeforeTheSessionIsSeeded()
	{
		var emitter = new StreamlabsDesktopEventEmitter(_publisher);

		emitter.PublishSceneChanged("Gameplay", null);
		emitter.PublishStreamingStatus(StreamlabsStreamingState.Offline, StreamlabsStreamingState.Live);
		emitter.PublishStudioModeChanged(true);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void GoingLive_RaisesTheFineAndTheCoarseEvent()
	{
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Offline, StreamlabsStreamingState.Starting);
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Starting, StreamlabsStreamingState.Live);

		Assert.Multiple(() =>
		{
			Assert.That(Ids(),
				Is.EqualTo(new[]
				{
					StreamlabsDesktopEventIds.StreamingStatusChanged,
					StreamlabsDesktopEventIds.StreamingStatusChanged,
					StreamlabsDesktopEventIds.StreamingStarted
				}));
			Assert.That(_publisher.Published[1].Parameters?["status"], Is.EqualTo("live"));
			Assert.That(_publisher.Published[1].Parameters?["previousStatus"], Is.EqualTo("starting"));
		});
	}

	[Test]
	public void AReconnect_DoesNotLookLikeAStop()
	{
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Live, StreamlabsStreamingState.Reconnecting);
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Reconnecting, StreamlabsStreamingState.Live);

		Assert.That(Ids(), Is.All.EqualTo(StreamlabsDesktopEventIds.StreamingStatusChanged));
	}

	[Test]
	public void GoingOffline_RaisesStreamingStopped()
	{
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Live, StreamlabsStreamingState.Offline);

		Assert.That(Ids(), Does.Contain(StreamlabsDesktopEventIds.StreamingStopped));
	}

	[Test]
	public void AnUnchangedStatus_RaisesNothing()
	{
		_emitter.PublishStreamingStatus(StreamlabsStreamingState.Live, StreamlabsStreamingState.Live);
		_emitter.PublishRecordingStatus(StreamlabsRecordingState.Offline, StreamlabsRecordingState.Offline);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void Recording_RaisesTheCoarsePairOnTheTerminalStatesOnly()
	{
		_emitter.PublishRecordingStatus(StreamlabsRecordingState.Offline, StreamlabsRecordingState.Starting);
		_emitter.PublishRecordingStatus(StreamlabsRecordingState.Starting, StreamlabsRecordingState.Recording);
		_emitter.PublishRecordingStatus(StreamlabsRecordingState.Recording, StreamlabsRecordingState.Stopping);
		_emitter.PublishRecordingStatus(StreamlabsRecordingState.Stopping, StreamlabsRecordingState.Offline);

		Assert.That(Ids().Where(id => id != StreamlabsDesktopEventIds.RecordingStatusChanged),
			Is.EqualTo(new[]
			{
				StreamlabsDesktopEventIds.RecordingStarted,
				StreamlabsDesktopEventIds.RecordingStopped
			}));
	}

	[Test]
	public void SavingAReplay_IsNotAStopOfTheReplayBuffer()
	{
		_emitter.PublishReplayBufferStatus(StreamlabsReplayBufferState.Offline, StreamlabsReplayBufferState.Running);
		_emitter.PublishReplayBufferStatus(StreamlabsReplayBufferState.Running, StreamlabsReplayBufferState.Saving);
		_emitter.PublishReplayBufferStatus(StreamlabsReplayBufferState.Saving, StreamlabsReplayBufferState.Running);

		Assert.That(Ids(), Is.EqualTo(new[] { StreamlabsDesktopEventIds.ReplayBufferStarted }));
	}

	[Test]
	public void SceneChanged_CarriesThePreviousScene()
	{
		_emitter.PublishSceneChanged("BRB", "Gameplay");

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(StreamlabsDesktopEventIds.SceneChanged));
			Assert.That(_publisher.Published[0].Parameters?["sceneName"], Is.EqualTo("BRB"));
			Assert.That(_publisher.Published[0].Parameters?["previousSceneName"], Is.EqualTo("Gameplay"));
		});
	}

	[Test]
	public void SceneChanged_UsesAnEmptyPreviousWhenThereIsNone()
	{
		_emitter.PublishSceneChanged("BRB", null);

		Assert.That(_publisher.Published[0].Parameters?["previousSceneName"], Is.EqualTo(string.Empty));
	}

	[Test]
	public void RepeatedMutePushes_PublishOnlyOnTheChange()
	{
		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: true);
		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: true);
		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: false);

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published, Has.Count.EqualTo(2));
			Assert.That(_publisher.Published[0].Parameters?["muted"], Is.EqualTo(true));
			Assert.That(_publisher.Published[1].Parameters?["muted"], Is.EqualTo(false));
		});
	}

	[Test]
	public void TwoItemsSharingASourceName_DeduplicateIndependently()
	{
		_emitter.PublishSourceVisibilityChanged("scene", "item-a", "Gameplay", "Webcam", visible: false);
		_emitter.PublishSourceVisibilityChanged("scene", "item-b", "Gameplay", "Webcam", visible: false);

		Assert.That(_publisher.Published,
			Has.Count.EqualTo(2),
			"visibility is per scene item, not per source name");
	}

	[Test]
	public void Reset_ClosesTheGate()
	{
		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: true);
		_emitter.Reset();

		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: false);

		Assert.That(_publisher.Published, Has.Count.EqualTo(1), "nothing is published while the gate is closed");
	}

	[Test]
	public void Reset_ClearsTheDeduplicationTables()
	{
		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: true);
		_emitter.Reset();
		_emitter.MarkReady();

		_emitter.PublishSourceMuteChanged("mic", "Mic/Aux", muted: true);

		Assert.That(_publisher.Published,
			Has.Count.EqualTo(2),
			"the table was cleared, so the first push after reconnecting is reported");
	}

	[Test]
	public void ConnectionEvents_BypassTheGate()
	{
		var emitter = new StreamlabsDesktopEventEmitter(_publisher);

		emitter.PublishConnected();
		emitter.PublishDisconnected();

		Assert.That(Ids(),
			Is.EqualTo(new[] { StreamlabsDesktopEventIds.Connected, StreamlabsDesktopEventIds.Disconnected }));
	}

	[Test]
	public void StudioMode_CarriesTheEnabledFlag()
	{
		_emitter.PublishStudioModeChanged(true);

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(StreamlabsDesktopEventIds.StudioModeChanged));
			Assert.That(_publisher.Published[0].Parameters?["enabled"], Is.EqualTo(true));
		});
	}

	private string[] Ids() => _publisher.Published.Select(entry => entry.EventId).ToArray();

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
