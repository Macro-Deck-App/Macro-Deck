using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
internal sealed class VariableEventAudienceTests
{
	private static readonly Guid _timerWidget = Guid.NewGuid();
	private static readonly Guid _otherWidget = Guid.NewGuid();
	private static readonly EventTriggerOwner _automation = EventTriggerOwner.ForAutomation(Guid.NewGuid());

	private VariableRegistry _registry = null!;
	private VariableEventAudience _audience = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_audience = new VariableEventAudience(_registry);
	}

	private VariableEntity Add(string name, VariableScope scope, Guid? widget = null)
	{
		var variable = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = widget?.ToString(),
			Type = VariableType.Numeric,
			Classification = VariableClassification.User,
			Value = "0"
		};
		_registry.Upsert(variable);
		return variable;
	}

	private static EventOccurrence Changed(VariableEntity variable)
	{
		return new EventOccurrence(EventIds.Qualify(EventIds.VariableChanged),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["variable"] = variable.Name },
			null,
			null,
			0,
			EventVariableSource.Of(variable));
	}

	[Test]
	public void An_automation_watches_the_global_variable_and_not_a_widget_variable_of_the_same_name()
	{
		var global = Add("count", VariableScope.Global);
		var widget = Add("count", VariableScope.Widget, _timerWidget);

		Assert.Multiple(() =>
		{
			Assert.That(_audience.Reaches(_automation, Changed(global)), Is.True);
			Assert.That(_audience.Reaches(_automation, Changed(widget)), Is.False);
		});
	}

	[Test]
	public void A_widget_variable_without_a_global_namesake_still_reaches_automations()
	{
		var remaining = Add("countdown_remaining_seconds", VariableScope.Widget, _timerWidget);

		Assert.That(_audience.Reaches(_automation, Changed(remaining)), Is.True);
	}

	[Test]
	public void A_widget_watches_its_own_variable_before_the_global_one()
	{
		var global = Add("count", VariableScope.Global);
		var own = Add("count", VariableScope.Widget, _timerWidget);
		var listener = EventTriggerOwner.ForWidget(_timerWidget);

		Assert.Multiple(() =>
		{
			Assert.That(_audience.Reaches(listener, Changed(own)), Is.True);
			Assert.That(_audience.Reaches(listener, Changed(global)), Is.False);
		});
	}

	[Test]
	public void Another_widget_hears_a_widget_variable_only_when_the_name_means_nothing_else_to_it()
	{
		var timer = Add("elapsed", VariableScope.Widget, _timerWidget);
		var listener = EventTriggerOwner.ForWidget(_otherWidget);

		var unshadowed = _audience.Reaches(listener, Changed(timer));
		Add("elapsed", VariableScope.Widget, _otherWidget);
		var shadowed = _audience.Reaches(listener, Changed(timer));

		Assert.Multiple(() =>
		{
			Assert.That(unshadowed, Is.True);
			Assert.That(shadowed, Is.False);
		});
	}

	[Test]
	public void A_widget_without_a_variable_of_that_name_watches_the_global_one()
	{
		var global = Add("count", VariableScope.Global);

		Assert.That(_audience.Reaches(EventTriggerOwner.ForWidget(_otherWidget), Changed(global)), Is.True);
	}

	[Test]
	public void The_deleted_widget_variable_of_a_global_name_stays_away_from_automations()
	{
		Add("count", VariableScope.Global);
		var widget = Add("count", VariableScope.Widget, _timerWidget);
		_registry.Remove(widget.Id);

		var deleted = Changed(widget) with { EventId = EventIds.Qualify(EventIds.VariableDeleted) };

		Assert.That(_audience.Reaches(_automation, deleted), Is.False);
	}

	[Test]
	public void An_occurrence_without_a_variable_source_reaches_everyone()
	{
		Add("count", VariableScope.Global);
		var manual = new EventOccurrence(EventIds.Qualify(EventIds.VariableChanged),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["variable"] = "count" });

		Assert.That(_audience.Reaches(EventTriggerOwner.ForWidget(_timerWidget), manual), Is.True);
	}
}
