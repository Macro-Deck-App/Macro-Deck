using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class EventBindingTrackerTests
{
	private EventSubscriptionIndex _index = null!;
	private EventBindingTracker _tracker = null!;
	private List<(string ProviderId, IReadOnlyList<EventBindingDto> Bindings)> _delivered = null!;

	[SetUp]
	public void SetUp()
	{
		_index = new EventSubscriptionIndex(new StubFolderCache(), new StubAutomationCache());
		_tracker = new EventBindingTracker(_index, Serilog.Core.Logger.None);
		_delivered = [];
		_tracker.Subscribe(Record);
	}

	[TearDown]
	public void TearDown() => _tracker.Dispose();

	internal static string Flows(string providerId, string eventId, params object[] parameters)
		=> JsonSerializer.Serialize(new[]
		{
			new
			{
				triggerId = "t1",
				triggerType = "onEvent",
				@event = new { providerId, eventId, parameters },
				children = Array.Empty<object>()
			}
		});

	internal static object Combo(string key)
		=> new { name = "combo", value = new { modifiers = new[] { "Ctrl", "Shift" }, key }, @operator = "==" };

	[Test]
	public async Task A_bound_combo_reaches_its_provider_as_the_structured_value_the_user_authored()
	{
		_index.ReindexAutomation(Guid.NewGuid(), Flows("com.hotkeys", "hotkey-pressed", Combo("F3")), enabled: true);
		await _tracker.FlushAsync();

		var (providerId, bindings) = _delivered.Single();
		var combo = bindings.Single().Parameters["combo"];

		Assert.Multiple(() =>
		{
			Assert.That(providerId, Is.EqualTo("com.hotkeys"));
			Assert.That(bindings.Single().EventId, Is.EqualTo("hotkey-pressed"));
			Assert.That(combo.Operator, Is.EqualTo("=="));
			Assert.That(combo.Value!.Value.GetProperty("key").GetString(), Is.EqualTo("F3"));
			Assert.That(combo.Value!.Value.GetProperty("modifiers")[1].GetString(), Is.EqualTo("Shift"));
			Assert.That(_tracker.BindingsFor("com.hotkeys"), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_provider_never_sees_another_providers_triggers()
	{
		_index.ReindexAutomation(Guid.NewGuid(), Flows("com.other", "scene-changed", Combo("F4")), enabled: true);
		await _tracker.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_delivered.Select(d => d.ProviderId), Is.EqualTo(new[] { "com.other" }));
			Assert.That(_tracker.BindingsFor("com.hotkeys"), Is.Empty);
		});
	}

	[Test]
	public async Task A_state_operator_binding_carries_no_value()
	{
		_index.ReindexAutomation(Guid.NewGuid(),
			Flows("com.hotkeys", "hotkey-pressed", new { name = "combo", @operator = "isNotEmpty" }),
			enabled: true);
		await _tracker.FlushAsync();

		var combo = _delivered.Single().Bindings.Single().Parameters["combo"];

		Assert.Multiple(() =>
		{
			Assert.That(combo.Operator, Is.EqualTo("isNotEmpty"));
			Assert.That(combo.Value, Is.Null);
		});
	}

	[Test]
	public async Task An_index_change_that_leaves_a_providers_bindings_identical_is_not_delivered_again()
	{
		var automationId = Guid.NewGuid();
		var flows = Flows("com.hotkeys", "hotkey-pressed", Combo("F3"));
		_index.ReindexAutomation(automationId, flows, enabled: true);
		await _tracker.FlushAsync();
		_delivered.Clear();

		_index.ReindexAutomation(automationId, flows, enabled: true);
		await _tracker.FlushAsync();

		Assert.That(_delivered, Is.Empty);
	}

	[Test]
	public async Task Disabling_the_last_bound_automation_delivers_an_empty_list()
	{
		var automationId = Guid.NewGuid();
		var flows = Flows("com.hotkeys", "hotkey-pressed", Combo("F3"));
		_index.ReindexAutomation(automationId, flows, enabled: true);
		await _tracker.FlushAsync();

		_index.ReindexAutomation(automationId, flows, enabled: false);
		await _tracker.FlushAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_delivered, Has.Count.EqualTo(2));
			Assert.That(_delivered[^1].ProviderId, Is.EqualTo("com.hotkeys"));
			Assert.That(_delivered[^1].Bindings, Is.Empty);
		});
	}

	[Test]
	public async Task A_listener_that_throws_does_not_stop_delivery_to_the_others()
	{
		using var tracker = new EventBindingTracker(_index, Serilog.Core.Logger.None);
		var reached = new List<string>();
		tracker.Subscribe((_, _) => throw new InvalidOperationException("listener failure"));
		tracker.Subscribe((providerId, _) =>
		{
			reached.Add(providerId);
			return Task.CompletedTask;
		});

		_index.ReindexAutomation(Guid.NewGuid(), Flows("com.hotkeys", "hotkey-pressed", Combo("F3")), enabled: true);
		await tracker.FlushAsync();

		Assert.That(reached, Is.EqualTo(new[] { "com.hotkeys" }));
	}

	private Task Record(string providerId, IReadOnlyList<EventBindingDto> bindings)
	{
		_delivered.Add((providerId, bindings));
		return Task.CompletedTask;
	}
}
