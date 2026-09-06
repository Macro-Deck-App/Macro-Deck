namespace MacroDeck.Sdk.Variables;

/// <summary>Manages variables owned by the calling integration.</summary>
/// <remarks>The host scopes this API to the integration, preventing access to variables owned elsewhere.</remarks>
public interface IVariableApi
{
	/// <summary>Returns all global variables owned by the integration.</summary>
	Task<IReadOnlyList<VariableHandle>> GetAllAsync();

	/// <summary>Returns an owned variable by canonical name, or <c>null</c> when none exists.</summary>
	Task<VariableHandle?> GetByNameAsync(string name);

	/// <summary>
	/// Creates a variable. Use <paramref name="definitionId"/> for stable catalog variables; otherwise the host derives it from the name.
	/// </summary>
	Task<VariableHandle> CreateAsync(
		string name,
		VariableType type,
		object? initialValue = null,
		int? decimalPlaces = null,
		string? definitionId = null);

	/// <summary>
	/// Creates a variable from a full declaration, so its display name, attributes and configuration reach
	/// the host as they would for a polled variable. <see cref="VariableDefinition.RefreshInterval"/> is
	/// ignored: a pushed variable has no polling cadence.
	/// </summary>
	/// <remarks>
	/// Defaults to the name-and-type overload, dropping the extra metadata, so a host built before this
	/// existed still satisfies the contract. That fallback needs a <see cref="VariableDefinition.Name"/>
	/// and creates nothing usable without one. An overload rather than optional parameters on the existing
	/// method, which would break every already-compiled caller.
	/// </remarks>
	Task<VariableHandle> CreateAsync(VariableDefinition declaration, object? initialValue = null)
		=> CreateAsync(declaration.Name ?? string.Empty,
			declaration.Type,
			initialValue,
			declaration.DecimalPlaces,
			declaration.Id);

	/// <summary>Updates an owned variable's value.</summary>
	Task SetValueAsync(Guid variableId, object? value);

	/// <summary>Deletes an owned variable. Unknown or foreign IDs are ignored.</summary>
	Task DeleteAsync(Guid variableId);
}
