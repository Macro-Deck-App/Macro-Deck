namespace MacroDeckHost.Application.Triggers;

public enum EventTriggerOwnerKind
{
	Widget,
	Automation
}

public readonly record struct EventTriggerOwner(EventTriggerOwnerKind Kind, Guid Id)
{
	public static EventTriggerOwner ForWidget(Guid widgetId) => new(EventTriggerOwnerKind.Widget, widgetId);

	public static EventTriggerOwner ForAutomation(Guid automationId)
		=> new(EventTriggerOwnerKind.Automation, automationId);

	public override string ToString() => $"{Kind}/{Id}";
}
