namespace MacroDeckHost.Application.Secrets.Approvals;

public enum FlowOwnerKind
{
	Widget,
	Automation,
	Script
}

public readonly record struct FlowOwner(FlowOwnerKind Kind, Guid Id)
{
	public static FlowOwner Widget(Guid id)
	{
		return new FlowOwner(FlowOwnerKind.Widget, id);
	}

	public static FlowOwner Automation(Guid id)
	{
		return new FlowOwner(FlowOwnerKind.Automation, id);
	}

	public static FlowOwner Script(Guid id)
	{
		return new FlowOwner(FlowOwnerKind.Script, id);
	}
}
