using System.Text.Json;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventSubscriptionIndexTests
{
	private StubFolderCache _cache = null!;
	private StubAutomationCache _automations = null!;
	private EventSubscriptionIndex _index = null!;

	[SetUp]
	public void SetUp()
	{
		_cache = new StubFolderCache();
		_automations = new StubAutomationCache();
		_index = new EventSubscriptionIndex(_cache, _automations);
	}

	private static string WidgetData(params (string TriggerId, string ProviderId, string EventId)[] triggers)
		=> JsonSerializer.Serialize(new { flows = AutomationFlows(triggers) });

	private static string AutomationFlows(params (string TriggerId, string ProviderId, string EventId)[] triggers)
	{
		var flows = triggers.Select(t => new
		{
			triggerId = t.TriggerId,
			triggerType = "onEvent",
			@event = new
			{
				providerId = t.ProviderId,
				eventId = t.EventId,
				parameters = new[] { new { name = "sceneName", type = "string", value = "Live" } }
			},
			children = Array.Empty<object>()
		});

		return JsonSerializer.Serialize(flows);
	}

	[Test]
	public void Rebuild_indexes_event_triggers_from_escaped_flow_json()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		_cache.AddFolder(widget);

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);
			Assert.That(_index.Find("obs::scene-changed"), Has.Count.EqualTo(1));
			Assert.That(_index.Find("obs::scene-changed")[0].Owner, Is.EqualTo(EventTriggerOwner.ForWidget(widget.Id)));
			Assert.That(_index.Find("obs::scene-changed")[0].Configuration["sceneName"].Value.GetString(),
				Is.EqualTo("Live"));
		});
	}

	[Test]
	public void One_widget_can_hold_several_event_triggers()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed"),
			("t2", "time", "interval")));
		_cache.AddFolder(widget);

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);
			Assert.That(_index.HasSubscribers("time::interval"), Is.True);
			var target = new EventTarget(EventTriggerOwner.ForWidget(widget.Id), "t2");
			Assert.That(_index.Find(target)?.EventId, Is.EqualTo("time::interval"));
		});
	}

	[Test]
	public void Several_widgets_can_subscribe_to_one_event()
	{
		var first = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		var second = StubFolderCache.Widget(WidgetData(("t2", "obs", "scene-changed")));
		_cache.AddFolder(first, second);

		_index.Rebuild();

		Assert.That(_index.Find("obs::scene-changed"), Has.Count.EqualTo(2));
	}

	[Test]
	public void Reindex_adds_and_removes_a_single_widget()
	{
		var widget = StubFolderCache.Widget(null);
		_cache.AddFolder(widget);
		_index.Rebuild();

		_index.ReindexWidget(widget.Id, WidgetData(("t1", "obs", "scene-changed")));
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);

		_index.ReindexWidget(widget.Id, """{"flows":"[]"}""");
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void Remove_drops_the_widgets_subscriptions()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		_cache.AddFolder(widget);
		_index.Rebuild();

		_index.Remove(EventTriggerOwner.ForWidget(widget.Id));

		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void Rebuild_clears_subscriptions_of_a_folder_that_already_left_the_cache()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		var folder = _cache.AddFolder(widget);
		_index.Rebuild();
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);

		_cache.RemoveFolder(folder.Id);
		_index.Rebuild();

		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void A_trigger_with_no_event_bound_yet_is_not_a_subscription()
	{
		const string data = """
							{"flows":"[{\"triggerId\":\"t1\",\"triggerType\":\"onEvent\",\"children\":[]}]"}
							""";
		_index.ReindexWidget(Guid.NewGuid(), data);

		Assert.That(_index.FindByProvider("obs"), Is.Empty);
	}

	[Test]
	public void Press_triggers_are_not_indexed()
	{
		const string data = """
							{"flows":"[{\"triggerId\":\"t1\",\"triggerType\":\"onShortPress\",\"children\":[]}]"}
							""";
		_index.ReindexWidget(Guid.NewGuid(), data);

		Assert.That(_index.FindByProvider("obs"), Is.Empty);
	}

	[Test]
	public void Malformed_widget_data_is_ignored_rather_than_throwing()
	{
		Assert.DoesNotThrow(() => _index.ReindexWidget(Guid.NewGuid(), "{ not json"));
		Assert.DoesNotThrow(() => _index.ReindexWidget(Guid.NewGuid(), """{"flows":"onEvent but not json"}"""));
	}

	[Test]
	public void FindByProvider_returns_only_that_providers_subscriptions()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed"),
			("t2", "time", "interval")));
		_cache.AddFolder(widget);
		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.FindByProvider("time"), Has.Count.EqualTo(1));
			Assert.That(_index.FindByProvider("time")[0].TriggerId, Is.EqualTo("t2"));
		});
	}

	[Test]
	public void Changed_fires_when_the_index_actually_changes()
	{
		var changes = 0;
		_index.Changed += () => changes++;

		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		_index.ReindexWidget(widget.Id, widget.Data);

		Assert.That(changes, Is.EqualTo(1));
	}

	[Test]
	public void Reindexing_a_widget_without_event_triggers_is_a_no_op()
	{
		var changes = 0;
		_index.Changed += () => changes++;

		_index.ReindexWidget(Guid.NewGuid(), """{"isToggled":true}""");

		Assert.That(changes, Is.Zero);
	}

	[Test]
	public void Rebuild_indexes_an_automations_trigger_from_its_bare_flow_array()
	{
		var automation = _automations.Add(AutomationFlows(("t1", "obs", "scene-changed")));

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);
			Assert.That(_index.Find("obs::scene-changed")[0].Owner,
				Is.EqualTo(EventTriggerOwner.ForAutomation(automation.Id)));
			Assert.That(_index.Find("obs::scene-changed")[0].Configuration["sceneName"].Value.GetString(),
				Is.EqualTo("Live"));
		});
	}

	[Test]
	public void A_widget_and_an_automation_subscribe_to_the_same_event_side_by_side()
	{
		var widget = StubFolderCache.Widget(WidgetData(("t1", "obs", "scene-changed")));
		_cache.AddFolder(widget);
		var automation = _automations.Add(AutomationFlows(("t2", "obs", "scene-changed")));

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.Find("obs::scene-changed"), Has.Count.EqualTo(2));
			Assert.That(_index.Find("obs::scene-changed").Select(s => s.Owner),
				Is.EquivalentTo(new[]
				{
					EventTriggerOwner.ForWidget(widget.Id),
					EventTriggerOwner.ForAutomation(automation.Id)
				}));
		});
	}

	[Test]
	public void A_disabled_automation_contributes_no_subscriptions()
	{
		_automations.Add(AutomationFlows(("t1", "obs", "scene-changed")), enabled: false);

		_index.Rebuild();

		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void Disabling_an_automation_drops_its_subscriptions()
	{
		var flows = AutomationFlows(("t1", "obs", "scene-changed"));
		var automation = _automations.Add(flows);
		_index.Rebuild();
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);

		_index.ReindexAutomation(automation.Id, flows, enabled: false);

		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void ReindexAutomation_adds_and_removes_a_single_automation()
	{
		var automation = _automations.Add(null);
		_index.Rebuild();

		_index.ReindexAutomation(automation.Id, AutomationFlows(("t1", "obs", "scene-changed")), enabled: true);
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);

		_index.ReindexAutomation(automation.Id, "[]", enabled: true);
		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void Remove_drops_an_automations_subscriptions()
	{
		var automation = _automations.Add(AutomationFlows(("t1", "obs", "scene-changed")));
		_index.Rebuild();

		_index.Remove(EventTriggerOwner.ForAutomation(automation.Id));

		Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.False);
	}

	[Test]
	public void FindByProvider_includes_an_automations_scheduled_trigger()
	{
		var automation = _automations.Add(AutomationFlows(("t1", "time", "interval")));

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.FindByProvider("time"), Has.Count.EqualTo(1));
			Assert.That(_index.FindByProvider("time")[0].Owner,
				Is.EqualTo(EventTriggerOwner.ForAutomation(automation.Id)));
		});
	}

	[Test]
	public void Only_an_automations_first_trigger_is_indexed()
	{
		var flows = AutomationFlows(("t1", "obs", "scene-changed"), ("t2", "time", "interval"));
		_automations.Add(flows);

		_index.Rebuild();

		Assert.Multiple(() =>
		{
			Assert.That(_index.HasSubscribers("obs::scene-changed"), Is.True);
			Assert.That(_index.HasSubscribers("time::interval"), Is.False);
		});
	}

	[Test]
	public void Malformed_automation_flows_are_ignored_rather_than_throwing()
	{
		var id = Guid.NewGuid();

		Assert.Multiple(() =>
		{
			Assert.DoesNotThrow(() => _index.ReindexAutomation(id, "[ not json", enabled: true));
			Assert.DoesNotThrow(() => _index.ReindexAutomation(id, "onEvent but not json", enabled: true));
		});
	}
}
