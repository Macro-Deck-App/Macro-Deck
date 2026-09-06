using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Automations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class InMemoryAutomationStore : IAutomationStore
{
	private readonly Dictionary<Guid, AutomationFile> _files = new();

	public InMemoryAutomationStore(params AutomationFile[] initial)
	{
		foreach (var file in initial)
		{
			_files[file.Id] = file;
		}
	}

	public int SaveCount { get; private set; }
	public int DeleteCount { get; private set; }

	public IReadOnlyList<AutomationFile> LoadAll() => _files.Values.ToList();

	public void Save(AutomationFile automation)
	{
		_files[automation.Id] = automation;
		SaveCount++;
	}

	public void Delete(Guid id)
	{
		_files.Remove(id);
		DeleteCount++;
	}

	public AutomationFile? Get(Guid id) => _files.GetValueOrDefault(id);
}
