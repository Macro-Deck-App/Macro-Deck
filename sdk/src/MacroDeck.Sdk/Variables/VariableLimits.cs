namespace MacroDeck.Sdk.Variables;

/// <summary>
/// The bounds the host enforces on a provider's variable surface. Exceeding one is never a registration
/// failure: the host clamps or drops the offending part and logs it, so one tail-end mistake cannot take
/// an integration's whole variable surface offline.
/// </summary>
public static class VariableLimits
{
	/// <summary>
	/// How many <see cref="VariableMaterialization.Eager"/> definitions one provider may register.
	/// Beyond it the host keeps the first <see cref="MaxEagerVariablesPerProvider"/> in declaration
	/// order and drops the rest: an eager variable is polled forever, so the eager set is the one that
	/// has to stay bounded. A provider with more to offer exposes them through
	/// <see cref="IVariableProvider.DiscoverAsync"/> instead.
	/// </summary>
	public const int MaxEagerVariablesPerProvider = 256;

	/// <summary>How many entries of <see cref="VariableDefinition.Attributes"/> the host keeps.</summary>
	public const int MaxAttributeEntries = 32;

	/// <summary>How long one attribute value may be. Attribute maps are broadcast to every connected
	/// client alongside the value, so they are bounded well below one message.</summary>
	public const int MaxAttributeValueLength = 256;
}
