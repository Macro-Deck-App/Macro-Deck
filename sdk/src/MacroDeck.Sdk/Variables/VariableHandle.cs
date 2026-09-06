namespace MacroDeck.Sdk.Variables;

/// <summary>
/// Snapshot of a variable as exposed to integrations. The id can be used
/// to update or delete the variable; the name is the canonical short
/// form (without the public <c>vars.</c> prefix).
/// </summary>
public sealed record VariableHandle(
	Guid Id,
	string Name,
	VariableType Type,
	object? Value,
	int? DecimalPlaces)
{
	/// <summary>
	/// The variable's stable owner-declared identity, when it has one. Unlike <see cref="Id"/>, which
	/// is regenerated whenever the host rebuilds its registry, this survives a restart and is what a
	/// reconnecting provider looks itself up by.
	/// </summary>
	public string? DefinitionId { get; init; }
}
