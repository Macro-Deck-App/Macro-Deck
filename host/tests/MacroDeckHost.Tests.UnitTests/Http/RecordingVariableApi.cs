using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Http;

internal sealed class RecordingVariableApi : IVariableApi
{
	private readonly Dictionary<Guid, string> _names = [];

	public Dictionary<string, object?> Written { get; } = new(StringComparer.Ordinal);

	public bool FailWrites { get; set; }

	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		=> Task.FromResult<IReadOnlyList<VariableHandle>>([]);

	public Task<VariableHandle?> GetByNameAsync(string name)
		=> Task.FromResult<VariableHandle?>(null);

	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		if (FailWrites)
		{
			throw new InvalidOperationException($"A variable named '{name}' already exists.");
		}

		var id = Guid.NewGuid();
		_names[id] = name;
		Written[name] = initialValue;
		return Task.FromResult(new VariableHandle(id, name, type, initialValue, decimalPlaces)
		{
			DefinitionId = definitionId
		});
	}

	public Task SetValueAsync(Guid variableId, object? value)
	{
		if (_names.TryGetValue(variableId, out var name))
		{
			Written[name] = value;
		}

		return Task.CompletedTask;
	}

	public Task DeleteAsync(Guid variableId) => Task.CompletedTask;
}
