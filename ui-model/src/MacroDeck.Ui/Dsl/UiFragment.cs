namespace MacroDeck.Ui.Dsl;

/// <summary>
/// A keyed, transparent grouping of children with no chrome of its own - a way to compose a handful of
/// elements as one unit (for example inside a <see cref="UiWhen" />) without introducing a node or a path
/// segment. <see cref="UiElement.Key" /> exists only for scope identity, duplicate detection and
/// diagnostics.
/// </summary>
public sealed record UiFragment : UiElement
{
	/// <summary>The fragment's children, in render order. A null element in an initializer-supplied
	/// collection is dropped - see <see cref="UiElementLists.DropNullElements" />.</summary>
	public IReadOnlyList<UiElement> Children
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];

	/// <summary>Never materialized: <see cref="UiFragment" /> emits no node of its own. Present only
	/// because <see cref="UiElement.Type" /> is abstract.</summary>
	public override string Type => "ui-fragment";
}
