using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Scripts;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class InMemoryScriptStore : IScriptStore
{
	private readonly Dictionary<Guid, ScriptFile> _files = new();

	public InMemoryScriptStore(params ScriptFile[] initial)
	{
		foreach (var file in initial)
		{
			_files[file.Id] = file;
		}
	}

	public int SaveCount { get; private set; }
	public int DeleteCount { get; private set; }

	public IReadOnlyList<ScriptFile> LoadAll() => _files.Values.ToList();

	public void Save(ScriptFile script)
	{
		_files[script.Id] = script;
		SaveCount++;
	}

	public void Delete(Guid id)
	{
		_files.Remove(id);
		DeleteCount++;
	}

	public ScriptFile? Get(Guid id) => _files.TryGetValue(id, out var file) ? file : null;
}
