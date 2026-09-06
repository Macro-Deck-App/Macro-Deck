using MacroDeckHost.Integrations.Voicemeeter;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterEventEmitterTests
{
	private RecordingPublisher _publisher = null!;
	private VoicemeeterEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new VoicemeeterEventEmitter(_publisher);
	}

	[Test]
	public void The_first_snapshot_reports_the_connection_with_its_edition()
	{
		_emitter.Observe(State());

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.Connected }));
			Assert.That(_publisher.Published[0].Parameters?["edition"], Is.EqualTo("Voicemeeter Banana"));
			Assert.That(_publisher.Published[0].Parameters?["version"], Is.EqualTo("3.0.2.8"));
		});
	}

	[Test]
	public void A_mute_change_carries_the_channel_name()
	{
		_emitter.Observe(State(Strip(0, label: "Mic")));
		_publisher.Clear();

		_emitter.Observe(State(Strip(0, label: "Mic", muted: true)));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.StripMuteChanged }));
			Assert.That(_publisher.Published[0].Parameters?["strip"], Is.EqualTo(0));
			Assert.That(_publisher.Published[0].Parameters?["name"], Is.EqualTo("Mic"));
			Assert.That(_publisher.Published[0].Parameters?["muted"], Is.EqualTo(true));
		});
	}

	[Test]
	public void An_unlabelled_channel_falls_back_to_its_Voicemeeter_name()
	{
		_emitter.Observe(State(Strip(0), Strip(1), Strip(2)));
		_publisher.Clear();

		_emitter.Observe(State(Strip(0), Strip(1), Strip(2, muted: true)));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].Parameters?["name"], Is.EqualTo("Strip 3"));
			Assert.That(_publisher.Published[0].Parameters?["strip"], Is.EqualTo(2));
		});
	}

	[Test]
	public void A_level_change_carries_where_it_came_from_and_went_to()
	{
		_emitter.Observe(State(Strip(0, gain: 0f)));
		_publisher.Clear();

		_emitter.Observe(State(Strip(0, gain: -6f)));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.StripGainChanged }));
			Assert.That(_publisher.Published[0].Parameters?["gain"], Is.EqualTo(-6d));
			Assert.That(_publisher.Published[0].Parameters?["previousGain"], Is.EqualTo(0d));
		});
	}

	[Test]
	public void A_level_that_only_wobbled_is_not_a_change()
	{
		_emitter.Observe(State(Strip(0, gain: -6f)));
		_publisher.Clear();

		_emitter.Observe(State(Strip(0, gain: -6.02f)));

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void Repatching_a_strip_reports_the_bus_that_changed_and_only_that_one()
	{
		_emitter.Observe(State(Strip(0, label: "Game", a1: true, a2: true)));
		_publisher.Clear();

		_emitter.Observe(State(Strip(0, label: "Game", a1: true)));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.StripRoutingChanged }));
			Assert.That(_publisher.Published[0].Parameters?["strip"], Is.EqualTo(0));
			Assert.That(_publisher.Published[0].Parameters?["name"], Is.EqualTo("Game"));
			Assert.That(_publisher.Published[0].Parameters?["bus"], Is.EqualTo("A2"));
			Assert.That(_publisher.Published[0].Parameters?["enabled"], Is.EqualTo(false));
		});
	}

	[Test]
	public void A_disconnect_reports_once_and_swallows_the_rest_of_the_diff()
	{
		_emitter.Observe(State(Strip(0, muted: true)));
		_publisher.Clear();

		_emitter.Observe(VoicemeeterState.Disconnected);

		Assert.That(_publisher.Ids, Is.EqualTo(new[] { VoicemeeterEventIds.Disconnected }));
	}

	[Test]
	public void Nothing_is_reported_while_disconnected()
	{
		_emitter.Observe(VoicemeeterState.Disconnected);
		_emitter.Observe(VoicemeeterState.Disconnected);

		Assert.That(_publisher.Published, Is.Empty);
	}

	private static VoicemeeterState State(params VoicemeeterChannel[] strips) => new()
	{
		IsConnected = true,
		Edition = VoicemeeterEdition.Banana,
		Version = "3.0.2.8",
		Strips = strips,
		Buses = []
	};

	private static VoicemeeterChannel Strip(
		int index,
		string label = "",
		float gain = 0f,
		bool muted = false,
		bool a1 = false,
		bool a2 = false)
		=> new(index,
			VoicemeeterChannelKind.Strip,
			IsPhysical: true,
			label,
			gain,
			muted,
			Mono: false,
			Solo: false,
			new Dictionary<string, bool>(StringComparer.Ordinal) { ["A1"] = a1, ["A2"] = a2 });

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public IEnumerable<string> Ids => Published.Select(entry => entry.EventId);

		public void Clear() => Published.Clear();

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
