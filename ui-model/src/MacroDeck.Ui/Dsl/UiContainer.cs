namespace MacroDeck.Ui.Dsl;

/// <summary>
/// A structural element that groups children under a chrome node - a flow, a step, a stack, an
/// advanced-section. Its id is the dot-joined path of keys from the root, including its own key; see the
/// identity rules documented on <see cref="Runtime.UiViewBuilder" />. Not an input: an input nested inside
/// a <see cref="UiContainer" /> keeps its bare key, because <see cref="UiContainer" /> never opens an
/// input-id scope - only <see cref="UiInputContainer{T}" /> does.
/// </summary>
public abstract record UiContainer : UiElement
{
	/// <summary>The element's children, in render order. A null element in an initializer-supplied
	/// collection is dropped - see <see cref="UiElementLists.DropNullElements" />.</summary>
	public IReadOnlyList<UiElement> Children
	{
		get;
		init => field = UiElementLists.DropNullElements(value);
	} = [];
}
