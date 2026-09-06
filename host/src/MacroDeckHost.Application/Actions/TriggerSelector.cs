namespace MacroDeckHost.Application.Actions;

public readonly record struct TriggerSelector
{
	private TriggerSelector(string value, bool byTriggerId)
	{
		Value = value;
		ByTriggerId = byTriggerId;
	}

	public string Value { get; }

	public bool ByTriggerId { get; }

	public static TriggerSelector ByType(string triggerType) => new(triggerType, false);

	public static TriggerSelector ById(string triggerId) => new(triggerId, true);
}
