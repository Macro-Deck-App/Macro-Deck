using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.UserVariables"/>'s <c>apply</c>
/// operation. Mirrors <c>IUserVariableApi.ApplyAsync</c>'s parameters.</summary>
public sealed record UserVariablesApplyArguments
{
	public required string Name { get; init; }

	public string? OwnerWidgetId { get; init; }

	/// <summary>The running script, so its local variables resolve first. Optional: a host that predates
	/// script variables ignores it, and an older plugin never sends it.</summary>
	public string? ScriptId { get; init; }

	/// <summary>The running automation, so its local variables resolve first. Optional, like
	/// <see cref="ScriptId"/>.</summary>
	public string? AutomationId { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.UserVariableOperation</c> enumerant name, e.g. <c>"Toggle"</c> -
	/// see <see cref="VariablesCreateArguments.Type"/>'s remarks on why this is a plain string.</summary>
	public required string Operation { get; init; }

	public string? Value { get; init; }
}

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.UserVariables"/>'s <c>create</c>
/// operation. Mirrors <c>IUserVariableApi.CreateAsync</c>'s parameters.</summary>
public sealed record UserVariablesCreateArguments
{
	public required string Name { get; init; }

	public string? OwnerWidgetId { get; init; }

	/// <summary>The running script, so its local variables resolve first. Optional: a host that predates
	/// script variables ignores it, and an older plugin never sends it.</summary>
	public string? ScriptId { get; init; }

	/// <summary>The running automation, so its local variables resolve first. Optional, like
	/// <see cref="ScriptId"/>.</summary>
	public string? AutomationId { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name, e.g. <c>"Numeric"</c> -
	/// see <see cref="VariablesCreateArguments.Type"/>'s remarks on why this is a plain string.</summary>
	public required string Type { get; init; }

	public string? InitialValue { get; init; }

	public int? DecimalPlaces { get; init; }
}

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.UserVariables"/>'s <c>read</c>
/// operation. Mirrors <c>IUserVariableApi.ReadInContextAsync</c>'s parameters.</summary>
public sealed record UserVariablesReadArguments
{
	public required string Name { get; init; }

	public string? OwnerWidgetId { get; init; }

	public string? ScriptId { get; init; }

	public string? AutomationId { get; init; }
}

/// <summary>The answer to <see cref="HostOperations.UserVariables.Read"/>. Mirrors
/// <c>MacroDeck.Sdk.Variables.UserVariableReadResult</c>.</summary>
public sealed record UserVariablesReadResult
{
	/// <summary>The <c>MacroDeck.Sdk.Variables.UserVariableReadStatus</c> enumerant name, e.g.
	/// <c>"Found"</c>. A name the plugin does not know is read as unavailable.</summary>
	public required string Status { get; init; }

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name of the variable that was
	/// found, also when its value is unavailable.</summary>
	public string? Type { get; init; }

	/// <summary>The value when found: a JSON string, number or boolean by <see cref="Type"/>.</summary>
	public JsonElement? Value { get; init; }

	/// <summary>The value as a template renders it.</summary>
	public string? Text { get; init; }

	public string? Message { get; init; }
}
