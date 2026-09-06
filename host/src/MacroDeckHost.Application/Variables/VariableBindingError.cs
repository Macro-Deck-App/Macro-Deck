namespace MacroDeckHost.Application.Variables;

/// <summary>
/// Errors specific to binding a provider catalog resource. Kept separate from
/// <see cref="MacroDeckHost.Domain.Enums.VariableError"/> because none of its members fit
/// "the provider is gone" or "this id does not name anything" - the two shapes this workflow adds.
/// </summary>
public enum VariableBindingError
{
	/// <summary>The integration is not registered, not enabled, not initialized, or does not declare the
	/// variables capability, or does not offer a catalog.</summary>
	ProviderUnavailable,

	/// <summary>The provider does not recognise this id at all - not "gone right now", but invalid.</summary>
	Unresolvable,

	/// <summary>The resolved definition exists but is not one the user may bind.</summary>
	NotBindable,

	/// <summary>The resolved definition's id is not a legal resource id.</summary>
	InvalidResourceId,

	/// <summary>The requested or derived name is already held by an unrelated variable.</summary>
	AlreadyExists,

	/// <summary>The requested name does not sanitize to a legal variable name.</summary>
	InvalidName,

	/// <summary>No binding exists for the given variable.</summary>
	NotFound,

	/// <summary>The binding store could not be read or written durably, so the mutation was refused
	/// rather than risking a silent loss of existing bindings.</summary>
	StoreUnavailable
}
