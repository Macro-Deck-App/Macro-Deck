namespace MacroDeck.Sdk.Actions;

public sealed class DynamicOptionsContext
{
	public required string ParameterName { get; init; }

	public string? Filter { get; init; }

	public required IReadOnlyDictionary<string, object?> CurrentParameters { get; init; }
}
