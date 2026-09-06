namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.UserVariables"/>'s only
/// operation, <c>apply</c>. Mirrors <c>IUserVariableApi.ApplyAsync</c>'s parameters.</summary>
public sealed record UserVariablesApplyArguments
{
	public required string Name { get; init; }

	public string? OwnerWidgetId { get; init; }

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

	/// <summary>The <c>MacroDeck.Sdk.Variables.VariableType</c> enumerant name, e.g. <c>"Numeric"</c> -
	/// see <see cref="VariablesCreateArguments.Type"/>'s remarks on why this is a plain string.</summary>
	public required string Type { get; init; }

	public string? InitialValue { get; init; }

	public int? DecimalPlaces { get; init; }
}
