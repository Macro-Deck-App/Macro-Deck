namespace MacroDeck.Sdk.Variables;

/// <summary>
/// The <see cref="VariableDefinition.SemanticKind"/> values the host knows how to format. The set is
/// open: a kind outside it is rendered as a plain number followed by the variable's
/// <see cref="VariableDefinition.Unit"/>, which is also what a host built before a given kind existed
/// does, so naming one is never an error.
/// </summary>
public static class VariableSemanticKinds
{
	/// <summary>No special formatting - the value with its unit, if it has one.</summary>
	public const string None = "none";

	/// <summary>A number of seconds, rendered as <c>mm:ss</c> and widening to <c>h:mm:ss</c>. Any
	/// declared unit is ignored, because the rendering already carries it.</summary>
	public const string Duration = "duration";

	/// <summary>A percentage. The unit is still declared, because the symbol and its spacing differ
	/// between languages.</summary>
	public const string Percentage = "percentage";

	/// <summary>A count of bytes, rendered on the 1024 ladder. Declare it only for a value that really
	/// is in bytes - a reading already scaled to GB is <see cref="None"/> with a <c>GB</c> unit.</summary>
	public const string Bytes = "bytes";
}
