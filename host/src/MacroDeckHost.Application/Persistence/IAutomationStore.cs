using MacroDeckHost.Application.Persistence.Automations;

namespace MacroDeckHost.Application.Persistence;

public interface IAutomationStore
{
	IReadOnlyList<AutomationFile> LoadAll();

	void Save(AutomationFile automation);

	void Delete(Guid id);
}
