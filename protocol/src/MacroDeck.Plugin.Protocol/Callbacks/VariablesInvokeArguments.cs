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

/// <summary>Arguments for <c>history.open</c> on <see cref="HostApis.Variables" />.</summary>
public sealed record VariablesHistoryOpenArguments
{
	/// <summary>The canonical name of one of the calling plugin's own global variables.</summary>
	public required string Name { get; init; }

	/// <summary>How many samples to keep. The host clamps it to its own bounds.</summary>
	public required int Capacity { get; init; }
}

/// <summary>Result of <c>history.open</c>.</summary>
public sealed record VariablesHistoryOpenResult
{
	/// <summary>The window, for <c>history.read</c> and <c>history.close</c>. Only valid on the connection
	/// that opened it.</summary>
	public required string WindowId { get; init; }
}

/// <summary>Arguments for <c>history.read</c> and <c>history.close</c> on <see cref="HostApis.Variables" />.</summary>
public sealed record VariablesHistoryWindowArguments
{
	public required string WindowId { get; init; }
}

/// <summary>Result of <c>history.read</c>: the samples, oldest first. A window the host no longer knows
/// answers no result.</summary>
public sealed record VariablesHistoryReadResult
{
	public IReadOnlyList<double?> Values { get; init; } = [];
}

/// <summary>Optional arguments for <c>refresh</c> on <see cref="HostApis.Variables" />.</summary>
public sealed record VariablesRefreshArguments
{
	/// <summary>The definition ids of the declared variables and bound catalog resources to read again. Absent
	/// or empty asks for every declared variable. At most
	/// <see cref="Limits.ProtocolLimits.MaxVariableRefreshDefinitionIds" /> ids.</summary>
	public IReadOnlyList<string>? DefinitionIds { get; init; }
}

/// <summary>Arguments for <c>bind</c> on <see cref="HostApis.Variables" />.</summary>
public sealed record VariablesBindArguments
{
	/// <summary>The bindings to record, at most <see cref="Limits.ProtocolLimits.MaxVariableBindingsPerCall" />.
	/// They are recorded together or not at all.</summary>
	public required IReadOnlyList<VariableBindingRequestDto> Bindings { get; init; }
}

/// <summary>One binding of <see cref="VariablesBindArguments" />.</summary>
public sealed record VariableBindingRequestDto
{
	/// <summary>A resource of the calling plugin's variable catalog.</summary>
	public required string LocalResourceId { get; init; }

	/// <summary>The canonical variable name.</summary>
	public required string Name { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name, e.g. <c>"Text"</c>.</summary>
	public required string Type { get; init; }

	public int? DecimalPlaces { get; init; }

	public string? MigratedFrom { get; init; }
}

/// <summary>Result of <c>bind</c>: one outcome per binding, in request order.</summary>
public sealed record VariablesBindResult
{
	/// <summary>A <see cref="VariableBindingOutcomes" /> value per binding.</summary>
	public IReadOnlyList<string> Outcomes { get; init; } = [];
}

/// <summary>The outcome values of <see cref="VariablesBindResult.Outcomes" />.</summary>
public static class VariableBindingOutcomes
{
	/// <summary>The binding was recorded.</summary>
	public const string Bound = "bound";

	/// <summary>The plugin already had a binding for the resource.</summary>
	public const string AlreadyBound = "already-bound";
}
