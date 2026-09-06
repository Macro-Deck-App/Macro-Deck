using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Variables"/>'s <c>get</c>
/// operation. <c>list</c> and <c>delete</c>/<c>set</c> need no wrapper - a bare id is enough.</summary>
public sealed record VariablesGetArguments
{
	public required string Name { get; init; }
}

public sealed record VariablesCreateArguments
{
	public required string Name { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name, e.g. <c>"Numeric"</c> -
	/// a plain string rather than the enum itself: the wire's JSON options carry no string-enum
	/// converter, so an enum-typed property here would serialize as an undocumented integer.</summary>
	public required string Type { get; init; }

	public JsonElement? InitialValue { get; init; }

	public int? DecimalPlaces { get; init; }

	public string? DefinitionId { get; init; }
}

public sealed record VariablesSetArguments
{
	public required Guid VariableId { get; init; }

	public JsonElement? Value { get; init; }
}

public sealed record VariablesDeleteArguments
{
	public required Guid VariableId { get; init; }
}
