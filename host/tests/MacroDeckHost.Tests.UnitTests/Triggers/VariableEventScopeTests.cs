using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
internal sealed class VariableEventScopeTests
{
	private static VariableEntity Variable(VariableScope scope)
	{
		return new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "count",
			Scope = scope,
			ScopeRefId = scope == VariableScope.Global ? null : Guid.NewGuid().ToString(),
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "1"
		};
	}

	[TestCase(VariableScope.Global, 3)]
	[TestCase(VariableScope.Widget, 3)]
	[TestCase(VariableScope.Script, 0)]
	[TestCase(VariableScope.Automation, 0)]
	public async Task Variable_events_stay_silent_for_variables_of_a_script_or_automation(
		VariableScope scope,
		int expectedEvents)
	{
		var bus = new RecordingEventBus();
		var variable = Variable(scope);

		await new VariableValueChangedEventTriggerHandler(bus).Handle(
			new VariableValueChangedNotification(variable, "0"),
			CancellationToken.None);
		await new VariableCreatedEventTriggerHandler(bus).Handle(new VariableCreatedNotification(variable),
			CancellationToken.None);
		await new VariableDeletedEventTriggerHandler(bus).Handle(new VariableDeletedNotification(variable),
			CancellationToken.None);

		Assert.That(bus.Published, Has.Count.EqualTo(expectedEvents));
	}

	[Test]
	public async Task A_reported_widget_variable_named_like_a_global_one_does_not_reach_an_automation()
	{
		var registry = new VariableRegistry();
		registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "count",
			Scope = VariableScope.Global,
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "0"
		});
		var bus = new RecordingEventBus();
		var variable = Variable(VariableScope.Widget);

		await new VariableValueChangedEventTriggerHandler(bus).Handle(
			new VariableValueChangedNotification(variable, "0"),
			CancellationToken.None);
		await new VariableCreatedEventTriggerHandler(bus).Handle(new VariableCreatedNotification(variable),
			CancellationToken.None);
		await new VariableDeletedEventTriggerHandler(bus).Handle(new VariableDeletedNotification(variable),
			CancellationToken.None);

		var audience = new VariableEventAudience(registry);
		var automation = EventTriggerOwner.ForAutomation(Guid.NewGuid());

		Assert.That(bus.Published.Select(occurrence => audience.Reaches(automation, occurrence)),
			Is.EqualTo(new[] { false, false, false }));
	}
}
