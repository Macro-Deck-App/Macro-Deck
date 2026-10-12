namespace MacroDeck.Sdk.Variables;

/// <summary>A binding to record through <see cref="IVariableApi.BindAsync" />.</summary>
/// <param name="LocalResourceId">The catalog resource of the calling integration the variable reads.</param>
/// <param name="Name">The canonical variable name, see <see cref="VariableNames.IsValid" />.</param>
/// <param name="Type">The variable's type.</param>
public sealed record VariableBindingRequest(string LocalResourceId, string Name, VariableType Type)
{
	/// <summary>Decimal places a numeric variable shows, or <c>null</c> for the default.</summary>
	public int? DecimalPlaces { get; init; }

	/// <summary>An optional marker naming what the binding was migrated from, kept with the binding for
	/// diagnostics.</summary>
	public string? MigratedFrom { get; init; }
}

/// <summary>What <see cref="IVariableApi.BindAsync" /> did with one request.</summary>
public enum VariableBindingOutcome
{
	/// <summary>The binding was recorded.</summary>
	Bound,

	/// <summary>The integration already had a binding for the resource; nothing changed.</summary>
	AlreadyBound
}
