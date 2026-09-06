using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;

namespace MacroDeckHost.Domain.Entities;

// Everything a provider declares about a variable once, as opposed to what a reading carries every time:
// how it is presented, the static attributes the host formats with, and whether the owner accepts a write.
public sealed record VariableDeclaration
{
	public VariablePresentation? Presentation { get; init; }

	/// <summary>
	/// Digits shown for a numeric variable. Part of the declaration, not just a creation argument: a
	/// binding restored at boot carries whatever precision it was bound with, and only the provider's
	/// current definition knows the right one.
	/// </summary>
	public int? DecimalPlaces { get; init; }

	public string? Unit { get; init; }

	public string? SemanticKind { get; init; }

	public IReadOnlyDictionary<string, string>? Attributes { get; init; }

	public VariableWriteCapability? Write { get; init; }
}
