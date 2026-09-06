using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class RecordingVariableApi : IVariableApi
{
	private readonly Dictionary<Guid, VariableHandle> _byId = [];
	private readonly Dictionary<string, Guid> _byName = new(StringComparer.Ordinal);

	public int CreateCount { get; private set; }

	public IReadOnlyCollection<string> Names => _byName.Keys;

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		=> Task.FromResult<IReadOnlyList<VariableHandle>>(_byId.Values.ToList());

	public Task<VariableHandle?> GetByNameAsync(string name)
		=> Task.FromResult(_byName.TryGetValue(name, out var id) ? _byId[id] : null);

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		if (_byName.ContainsKey(name))
		{
			throw new InvalidOperationException($"A variable named '{name}' already exists.");
		}

		CreateCount++;
		var handle = new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
		{
			DefinitionId = definitionId
		};
		_byId[handle.Id] = handle;
		_byName[name] = handle.Id;
		return Task.FromResult(handle);
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		if (_byId.TryGetValue(variableId, out var existing))
		{
			_byId[variableId] = existing with { Value = value };
		}

		return Task.CompletedTask;
	}

	public Task DeleteAsync(Guid variableId)
	{
		if (_byId.Remove(variableId, out var removed))
		{
			_byName.Remove(removed.Name);
		}

		return Task.CompletedTask;
	}
}
