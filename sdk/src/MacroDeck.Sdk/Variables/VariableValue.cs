namespace MacroDeck.Sdk.Variables;

/// <summary>
/// One variable's reading tagged with the provider-local id it belongs to, for the paths that carry
/// several at once - <see cref="IVariableProvider.SubscribeAsync"/>'s reply and
/// <see cref="IVariableSink.PublishAsync"/>'s batch.
/// </summary>
public sealed record VariableValue
{
	/// <summary>The provider-local id this reading belongs to.</summary>
	public required string Id { get; init; }

	public required VariableReading Reading { get; init; }

	/// <summary>Convenience for the common case: a bare value with no bounds.</summary>
	public static VariableValue Of(string id, object? value)
		=> new() { Id = id, Reading = VariableReading.Of(value) };

	/// <summary>Tags an already-built reading with its id.</summary>
	public static VariableValue Of(string id, VariableReading reading)
		=> new() { Id = id, Reading = reading };

	/// <summary>Convenience for the variable-is-gone case.</summary>
	public static VariableValue Unavailable(string id)
		=> new() { Id = id, Reading = VariableReading.Unavailable };
}
