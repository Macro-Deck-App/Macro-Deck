namespace MacroDeck.Ui.Dsl;

/// <summary>A transparent conditional section that does not affect descendant node IDs.</summary>
public sealed record UiWhen : UiElement
{
	/// <summary>Evaluated on each materialization.</summary>
	public required Func<bool> Condition { get; init; }

	/// <summary>Produces content when <see cref="Condition"/> is true.</summary>
	public required Func<UiElement> Content { get; init; }

	public override string Type => "ui-when";
}
