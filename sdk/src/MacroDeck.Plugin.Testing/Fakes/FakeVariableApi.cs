using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IVariableApi" /> - the plugin's own variables, plain CRUD with no apply/status
/// semantics. (That belongs to the user's variables instead; see <see cref="FakeUserVariableApi" />. The
/// two are easy to confuse from the interface names alone - see that type's remarks.)
///
/// <para>
/// <see cref="Created" /> is a log of every successful <see cref="CreateAsync" /> call, in call order,
/// unaffected by later updates or deletes - it answers "how many times did the plugin create a
/// variable", which is what acceptance tests for an init routine typically want, not "what does the
/// plugin have right now" (that is <see cref="GetAllAsync" />).
/// </para>
/// </summary>
public sealed class FakeVariableApi : IVariableApi
{
	private readonly Lock _gate = new();
	private readonly Dictionary<Guid, VariableHandle> _variables = new();
	private readonly List<VariableHandle> _created = [];

	/// <summary>Every variable this fake has created, in call order, exactly as it was at creation time.</summary>
	public IReadOnlyList<VariableHandle> Created
	{
		get
		{
			lock (_gate)
			{
				return [.. _created];
			}
		}
	}

	/// <summary>
	/// Every variable created so far, with its current value - reflecting any <see cref="SetValueAsync" />
	/// since.
	/// </summary>
	public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
	{
		lock (_gate)
		{
			return Task.FromResult<IReadOnlyList<VariableHandle>>([.. _variables.Values]);
		}
	}

	/// <summary>
	/// Looks up a variable by canonical name. Returns <c>null</c> when none was created with that name.
	/// </summary>
	public Task<VariableHandle?> GetByNameAsync(string name)
	{
		lock (_gate)
		{
			var match = _variables.Values.FirstOrDefault(handle
				=> string.Equals(handle.Name, name, StringComparison.Ordinal));

			return Task.FromResult(match);
		}
	}

	/// <summary>
	/// Creates and records a variable. Unlike the real host, this never fails on a name collision - two
	/// variables with the same name can coexist here, and <see cref="GetByNameAsync" /> returns whichever
	/// was created first.
	/// </summary>
	public Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null)
	{
		var handle = new VariableHandle(Guid.NewGuid(), name, type, initialValue, decimalPlaces)
		{
			DefinitionId = definitionId ?? VariableDefinitionId.FromName(name)
		};

		lock (_gate)
		{
			_variables[handle.Id] = handle;
			_created.Add(handle);
		}

		return Task.FromResult(handle);
	}

	/// <summary>
	/// Updates the value of a tracked variable. Mirrors the real host - which backs
	/// <see cref="IVariableApi" /> with a store an id can outlive - by throwing rather than silently
	/// dropping a write nobody will ever see.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="variableId" /> was never created, or was since deleted.
	/// </exception>
	public Task SetValueAsync(Guid variableId, object? value)
	{
		lock (_gate)
		{
			if (!_variables.TryGetValue(variableId, out var existing))
			{
				throw new InvalidOperationException($"Variable {variableId} does not exist; the value was not stored.");
			}

			_variables[variableId] = existing with { Value = value };
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(Guid variableId)
	{
		lock (_gate)
		{
			_variables.Remove(variableId);
		}

		return Task.CompletedTask;
	}
}
