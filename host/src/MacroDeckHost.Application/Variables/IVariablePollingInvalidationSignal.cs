namespace MacroDeckHost.Application.Variables;

public interface IVariablePollingInvalidationSignal
{
	void MarkStale(string integrationId);

	IReadOnlyCollection<string> DrainStale();
}
