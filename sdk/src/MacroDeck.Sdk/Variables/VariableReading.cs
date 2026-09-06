namespace MacroDeck.Sdk.Variables;

/// <summary>
/// One variable's value at one moment, together with the bounds that apply to it right now. A record
/// rather than a bare <c>object?</c> so that "the value is null" is unambiguously "this variable is
/// unavailable right now" and not "no value was supplied", and so that a later addition is an init
/// property rather than a break.
/// </summary>
public sealed record VariableReading
{
	/// <summary>
	/// A <see cref="string"/>, a number, or a <see cref="bool"/> matching the variable's declared
	/// <see cref="VariableDefinition.Type"/>; <c>null</c> when it is currently unavailable. Any other
	/// CLR value is treated as unavailable.
	/// </summary>
	public object? Value { get; init; }

	/// <summary>
	/// The lowest value a write may sensibly carry right now, or <c>null</c> when the provider declares
	/// no lower bound. Volatile on purpose - a seek position's range changes with the track - which is
	/// why it lives on the reading rather than on <see cref="VariableDefinition"/>.
	/// </summary>
	public double? Min { get; init; }

	/// <summary>The highest value a write may sensibly carry right now, or <c>null</c> for no upper bound.</summary>
	public double? Max { get; init; }

	/// <summary>The granularity a control should move in, or <c>null</c> when the provider declares none.</summary>
	public double? Step { get; init; }

	/// <summary>
	/// A reading that carries no bounds - the shape a variable with nothing adjustable about it returns.
	/// </summary>
	public static VariableReading Of(object? value) => new() { Value = value };

	/// <summary>A reading that also reports the bounds that apply to this value right now.</summary>
	public static VariableReading Of(object? value, double? min, double? max, double? step)
		=> new() { Value = value, Min = min, Max = max, Step = step };

	/// <summary>
	/// "Not available right now" - the reading a provider returns for a variable whose backing resource
	/// is disconnected, gone, or simply has nothing to report yet. Deliberately carries no bounds: a
	/// transient outage must not collapse a control's range to zero, so the host leaves whatever bounds
	/// it already knows in place rather than clearing them.
	/// </summary>
	public static readonly VariableReading Unavailable = new();
}
