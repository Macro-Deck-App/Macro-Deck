namespace MacroDeck.Sdk.Variables;

public enum VariableType
{
	Text = 0,
	Numeric = 1,
	Boolean = 2,

	/// <summary>A colour, carried as text: lowercase <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is not fully
	/// opaque. A Macro Deck release older than this member drops a definition that declares it.</summary>
	Color = 3,
}
