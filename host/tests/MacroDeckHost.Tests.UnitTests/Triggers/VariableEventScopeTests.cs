using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
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
}
