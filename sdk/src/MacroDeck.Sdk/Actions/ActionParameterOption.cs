using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

public sealed class ActionParameterOption
{
	public required string Value { get; init; }

	public LocalizedText Label { get; init; }

	/// <summary>
	/// Optional additive information for a picker option. Consumers that do not know a key ignore it,
	/// which keeps ordinary option sources and existing integrations wire-compatible.
	/// </summary>
	public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
