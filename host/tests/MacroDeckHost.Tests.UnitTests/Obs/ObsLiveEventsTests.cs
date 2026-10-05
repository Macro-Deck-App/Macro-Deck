using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsLiveEventsTests
{
	private static readonly TimeSpan _window = TimeSpan.FromMilliseconds(150);

	private FakeObsClient _fake = null!;
	private FakeTimeProvider _time = null!;
	private RecordingPublisher _publisher = null!;
	private List<IReadOnlyList<ObsTargetChange>> _batches = null!;
	private ObsConnection _connection = null!;

	[SetUp]
	public void SetUp()
	{
		_fake = new FakeObsClient { IsConnected = true, InputNames = ["Cam"] };
		_time = new FakeTimeProvider();
		_publisher = new RecordingPublisher();
		_batches = [];
		_connection = new ObsConnection(_fake,
			"ws://localhost:4455",
			null,
			events: new ObsEventEmitter(_publisher, Guid.NewGuid()),
			onTargetsChanged: _batches.Add,
			timeProvider: _time);
	}

	[TearDown]
	public void TearDown() => _connection.Dispose();

	[Test]
	public void A_burst_of_setting_changes_for_one_input_is_one_notification_and_no_status_query()
	{
		var queriesBefore = _fake.QueryStatusCount;

		for (var i = 0; i < 100; i++)
		{
			_fake.RaiseInputSettingsChanged("Cam", $"key{i % 3}");
		}

		_time.Advance(_window);

		Assert.Multiple(() =>
		{
			Assert.That(_batches, Has.Count.EqualTo(1));
			Assert.That(_batches[0], Has.Count.EqualTo(1));
			Assert.That(_batches[0][0].Keys, Is.EquivalentTo(new[] { "key0", "key1", "key2" }));
			Assert.That(_fake.QueryStatusCount, Is.EqualTo(queriesBefore));
		});
	}

	[Test]
	public void Changes_in_separate_windows_flush_separately()
	{
		_fake.RaiseInputSettingsChanged("Cam", "a");
		_time.Advance(_window);
		_fake.RaiseInputSettingsChanged("Cam", "b");
		_time.Advance(_window);

		Assert.That(_batches, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task A_settings_change_makes_the_next_read_fresh_instead_of_cached()
	{
		_fake.InputSettingsJson["Cam"] = """{"device":"one"}""";
		Assert.That(await _connection.GetInputSettingsJsonAsync("Cam"), Does.Contain("one"));

		_fake.InputSettingsJson["Cam"] = """{"device":"two"}""";
		Assert.That(await _connection.GetInputSettingsJsonAsync("Cam"), Does.Contain("one"), "still inside the cache window");

		_fake.RaiseInputSettingsChanged("Cam", "device");
		_time.Advance(_window);

		Assert.That(await _connection.GetInputSettingsJsonAsync("Cam"), Does.Contain("two"));
	}

	[Test]
	public async Task An_activity_change_makes_the_next_activity_read_fresh()
	{
		_fake.SourceActivity["Cam"] = new ObsSourceActivity(false, false);
		await _connection.GetSourceActiveAsync("Cam");
		_fake.SourceActivity["Cam"] = new ObsSourceActivity(true, true);

		_fake.RaiseInputActiveChanged("Cam", true);
		_time.Advance(_window);

		Assert.That((await _connection.GetSourceActiveAsync("Cam"))!.Active, Is.True);
	}

	[Test]
	public async Task A_filter_change_makes_the_next_filter_read_fresh_for_inputs_and_scenes()
	{
		_fake.InputNames = ["Cam", "Scene"];
		_fake.FilterStates["Scene::Blur"] = false;
		await _connection.GetSourceFilterEnabledCachedAsync("Scene", "Blur");
		_fake.FilterStates["Scene::Blur"] = true;

		_fake.RaiseSourceFilterChanged("Scene", "Blur");
		_time.Advance(_window);

		Assert.Multiple(async () =>
		{
			Assert.That(await _connection.GetSourceFilterEnabledCachedAsync("Scene", "Blur"), Is.True);
			var runtime = Guid.NewGuid();
			var ids = ObsVariableCatalog.DefinitionIdsFor(runtime, _batches[0]).ToList();
			Assert.That(ids, Has.Some.EndsWith("/filter/Blur/enabled").And.Contains("/input/"));
			Assert.That(ids, Has.Some.Contains("/scene/"));
		});
	}

	[Test]
	public void Definition_ids_name_the_settings_keys_and_both_activity_leaves_of_the_input()
	{
		var runtime = Guid.NewGuid();
		var ids = ObsVariableCatalog.DefinitionIdsFor(runtime,
		[
			new ObsTargetChange(ObsTargetKind.InputSettings, "Cam", null, ["device"]),
			new ObsTargetChange(ObsTargetKind.SourceActivity, "Cam")
		]).ToList();

		Assert.That(ids,
			Is.EquivalentTo(new[]
			{
				$"{runtime:D}/input/Cam/setting/device",
				$"{runtime:D}/input/Cam/active",
				$"{runtime:D}/input/Cam/showing"
			}));
	}

	[TestCase(true, false, "input-became-active")]
	[TestCase(false, false, "input-became-inactive")]
	[TestCase(true, true, "input-started-showing")]
	[TestCase(false, true, "input-stopped-showing")]
	public void Input_activity_events_publish_the_matching_trigger(bool value, bool showing, string expected)
	{
		if (showing)
		{
			_fake.RaiseInputShowingChanged("Cam", value);
		}
		else
		{
			_fake.RaiseInputActiveChanged("Cam", value);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published.Select(p => p.EventId), Is.EqualTo(new[] { expected }));
			Assert.That(_publisher.Published[0].Parameters!["inputName"], Is.EqualTo("Cam"));
		});
	}

	[Test]
	public void A_custom_event_publishes_name_data_and_fields_for_the_configuration()
	{
		_fake.RaiseCustomEvent("""{"eventName":"goal","team":"red"}""");

		var published = _publisher.Published.Single();
		Assert.Multiple(() =>
		{
			Assert.That(published.EventId, Is.EqualTo("custom-event"));
			Assert.That(published.Parameters!["eventName"], Is.EqualTo("goal"));
			Assert.That(published.Parameters["field_team"], Is.EqualTo("red"));
			Assert.That(published.Parameters.ContainsKey("configuration"), Is.True);
		});
	}

	[Test]
	public void Malformed_custom_events_publish_nothing_and_do_not_throw()
	{
		Assert.DoesNotThrow(() =>
		{
			_fake.RaiseCustomEvent("{ not json");
			_fake.RaiseCustomEvent(new string('x', ObsCustomEvent.MaxPayloadChars + 1));
			_fake.RaiseCustomEvent(string.Empty);
		});

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void A_flood_of_custom_events_is_capped()
	{
		for (var i = 0; i < 1000; i++)
		{
			_fake.RaiseCustomEvent("""{"eventName":"spam"}""");
		}

		Assert.That(_publisher.Published, Has.Count.LessThanOrEqualTo(50));
	}

	[Test]
	public void Nothing_is_published_or_flushed_after_dispose()
	{
		_fake.RaiseInputSettingsChanged("Cam", "a");
		_connection.Dispose();
		_time.Advance(_window);
		_fake.RaiseInputActiveChanged("Cam", true);
		_fake.RaiseCustomEvent("{}");

		Assert.Multiple(() =>
		{
			Assert.That(_batches, Is.Empty);
			Assert.That(_publisher.Published, Is.Empty);
		});
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
