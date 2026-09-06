namespace MacroDeckHost.Application.Persistence;

public interface IIntegrationStateStore
{
	IReadOnlyDictionary<string, bool> Load();

	void Save(IReadOnlyDictionary<string, bool> states);
}
