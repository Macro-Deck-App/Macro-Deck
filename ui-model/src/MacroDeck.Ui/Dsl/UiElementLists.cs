namespace MacroDeck.Ui.Dsl;

/// <summary>
/// Shared defensive-copy helper for every DSL element that carries a child list
/// (<see cref="UiContainer.Children" />, <see cref="UiInputContainer{T}.Children" />,
/// <see cref="UiFragment.Children" />), mirroring <see cref="Model.Nodes.UiNode.Children" />'s own
/// null-element normalization for the same underlying reason: a caller can pass a collection with a null
/// element, and every consumer walking the list expects a non-null <see cref="UiElement" />. Not a
/// System.Text.Json concern here - these lists are authored directly in code, never deserialized - but the
/// defensive posture is the same.
/// </summary>
internal static class UiElementLists
{
	/// <summary>Builds a defensive copy of <paramref name="elements" /> with any null element dropped.</summary>
	public static List<UiElement> DropNullElements(IReadOnlyList<UiElement>? elements)
	{
		var result = new List<UiElement>(elements?.Count ?? 0);

		if (elements is null)
		{
			return result;
		}

		foreach (var element in elements)
		{
			if (element is not null)
			{
				result.Add(element);
			}
		}

		return result;
	}
}
