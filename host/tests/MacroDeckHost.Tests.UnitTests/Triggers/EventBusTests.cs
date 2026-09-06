using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventBusTests
{
	private StubFolderCache _cache = null!;
	private StubAutomationCache _automations = null!;
	private EventSubscriptionIndex _index = null!;
	private EventSampleStore _samples = null!;
	private EventBus _bus = null!;

	[SetUp]
	public void SetUp()
	{
		_cache = new StubFolderCache();
		_automations = new StubAutomationCache();
		_index = new EventSubscriptionIndex(_cache, _automations);
		_samples = new EventSampleStore();
		_bus = new EventBus(_index, _samples, Log.Logger);
	}

	private static EventOccurrence Occurrence(string eventId, EventTarget? target = null)
		=> new(eventId,
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Live" },
			target);

	private void Subscribe(string providerId, string eventId)
	{
		var data = $$"""
					 {"flows":"[{\"triggerId\":\"t1\",\"triggerType\":\"onEvent\",\"event\":{\"providerId\":\"{{
						 providerId}}\",\"eventId\":\"{{eventId}}\"},\"children\":[]}]"}
					 """;
		_index.ReindexWidget(Guid.NewGuid(), data);
	}

	[Test]
	public void Publishing_with_no_subscribers_queues_nothing()
	{
		_bus.Publish(Occurrence("obs::scene-changed"));

		Assert.That(_bus.Reader.TryRead(out _), Is.False);
	}

	[Test]
	public void Publishing_with_a_subscriber_queues_the_occurrence()
	{
		Subscribe("obs", "scene-changed");

		_bus.Publish(Occurrence("obs::scene-changed"));

		Assert.Multiple(() =>
		{
			Assert.That(_bus.Reader.TryRead(out var queued), Is.True);
			Assert.That(queued!.EventId, Is.EqualTo("obs::scene-changed"));
		});
	}

	[Test]
	public void A_targeted_occurrence_is_queued_without_consulting_the_index()
	{
		_bus.Publish(Occurrence("time::interval", new EventTarget(EventTriggerOwner.ForWidget(Guid.NewGuid()), "t1")));

		Assert.That(_bus.Reader.TryRead(out _), Is.True);
	}

	[Test]
	public void The_last_occurrence_is_recorded_even_without_subscribers()
	{
		_bus.Publish(Occurrence("obs::scene-changed"));

		Assert.That(_samples.TryGetLast("obs::scene-changed")?["sceneName"], Is.EqualTo("Live"));
	}

	[Test]
	public void The_sample_store_keeps_only_the_most_recent_occurrence()
	{
		_bus.Publish(new EventOccurrence("obs::scene-changed",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "First" }));
		_bus.Publish(new EventOccurrence("obs::scene-changed",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["sceneName"] = "Second" }));

		Assert.That(_samples.TryGetLast("obs::scene-changed")?["sceneName"], Is.EqualTo("Second"));
	}

	[Test]
	public void An_event_that_never_fired_has_no_sample()
	{
		Assert.That(_samples.TryGetLast("obs::never"), Is.Null);
	}

	[Test]
	public void A_full_queue_drops_rather_than_blocking_the_publisher()
	{
		Subscribe("obs", "scene-changed");

		Assert.DoesNotThrow(() =>
		{
			for (var i = 0; i < 2_000; i++)
			{
				_bus.Publish(Occurrence("obs::scene-changed"));
			}
		});
	}
}
