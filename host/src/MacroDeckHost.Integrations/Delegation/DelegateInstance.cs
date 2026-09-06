namespace MacroDeckHost.Integrations.Delegation;

internal sealed record DelegateInstance(
	Guid EntryId,
	string InstanceId,
	string MachineName,
	string VariableKey,
	Uri BaseUrl,
	string Username,
	DateTimeOffset ConfiguredAt)
{
	public string Label => $"Macro Deck ({MachineName})";
}
