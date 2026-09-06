using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime.Internal;

namespace MacroDeck.Ui.Runtime;

/// <summary>
/// Builds a revision-0 <see cref="UiTree" /> from an authored <see cref="UiElement" /> tree, once and
/// without reacting to anything afterwards: a bound value is read exactly once, no
/// <see cref="UiState{T}" /> is subscribed to, and a later write cannot affect the returned tree. Use
/// <see cref="UiView" /> for a tree that keeps up with its state.
///
/// <para><b>The identity rules</b> - the contract every emitted <see cref="UiNode.Id" /> follows:</para>
/// <list type="bullet">
/// <item>A structural node's id (<see cref="UiContainer" />) is the dot-joined path of keys from the root,
/// including the root's own key.</item>
/// <item><see cref="UiWhen" /> and <see cref="UiFragment" /> are transparent: they emit no node and
/// contribute no path segment. Wrapping an element in either never changes that element's id.</item>
/// <item><see cref="UiRepeat{TItem}" /> is transparent too, but each item's key - from its
/// <c>KeySelector</c>, never its position - participates exactly as if that item's element appeared
/// directly at the repeat's position keyed with that item key.</item>
/// <item>An input's id (<see cref="UiInput{T}" />) is its bare key, with no structural prefix. The only
/// exception: an input nested inside a <see cref="UiInputContainer{T}" /> (an "object" or "array") is
/// addressed as <c>containerId.key</c>, and nesting composes.</item>
/// <item>Every composed id is validated against <see cref="Model.Identity.UiIdentifier.TryValidate" />; a
/// failure throws <see cref="UiViewException" /> naming the composed id - validation is of the composed
/// id, never of the bare key alone.</item>
/// <item>Ids are unique across the whole tree, including inside <see cref="UiElement.Fallback" />
/// subtrees; a duplicate throws <see cref="UiViewException" /> naming the id and both declaration paths.
/// </item>
/// <item><see cref="UiElement.RequiredComponentVersion" /> is emitted only when the author set it - a
/// <c>null</c> value is never materialized as an explicit <c>1</c>.</item>
/// </list>
/// </summary>
public static class UiViewBuilder
{
	/// <summary>Materializes <paramref name="root" /> into a <see cref="UiTree" /> at
	/// <see cref="UiTree.Revision" /> 0, rendered for <paramref name="surface" />. Throws
	/// <see cref="UiViewException" /> when the tree violates an identity rule - see this type's remarks.
	/// </summary>
	public static UiTree Build(UiSurface surface, UiElement root)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(root);

		var rootNode = new UiElementMaterializer(view: null).MaterializeRoot(root);

		return new UiTree { Revision = 0, Surface = surface, Root = rootNode.Node };
	}
}
