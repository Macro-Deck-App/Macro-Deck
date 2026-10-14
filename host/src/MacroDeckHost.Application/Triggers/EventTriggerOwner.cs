namespace MacroDeckHost.Application.Triggers;

public enum EventTriggerOwnerKind
{
	Widget,
	Automation
}

public readonly record struct EventTriggerOwner(EventTriggerOwnerKind Kind, Guid Id)
{
	public static EventTriggerOwner ForWidget(Guid widgetId)
	{
		return new EventTriggerOwner(EventTriggerOwnerKind.Widget, widgetId);
	}

	public static EventTriggerOwner ForAutomation(Guid automationId)
	{
		return new EventTriggerOwner(EventTriggerOwnerKind.Automation, automationId);
	}

	public override string ToString()
	{
		return $"{Kind}/{Id}";
	}
}
