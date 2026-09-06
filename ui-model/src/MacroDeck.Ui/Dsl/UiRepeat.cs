namespace MacroDeck.Ui.Dsl;

/// <summary>Repeats a template for items identified by stable keys without emitting a node of its own.</summary>
public sealed record UiRepeat<TItem> : UiElement, IUiRepeatElement
{
	/// <summary>Items to render. An absent value renders no items.</summary>
	public required UiValue<IReadOnlyList<TItem>> Items { get; init; }

	/// <summary>Returns the stable identity of an item. Do not derive it from the item's position.</summary>
	public required Func<TItem, string> KeySelector { get; init; }

	/// <summary>Builds an item's element and receives its composed item key.</summary>
	public required Func<TItem, string, UiElement> Template { get; init; }

	public override string Type => "ui-repeat";

	UiRepeatExpansion IUiRepeatElement.Expand(Func<string, string> composeItemKey)
	{
		if (!Items.TryEvaluate(out var items))
		{
			return new UiRepeatExpansion(itemList: null, []);
		}

		var expanded = new List<UiRepeatItem>();

		foreach (var item in items)
		{
			var current = item;
			var itemKey = KeySelector(current);
			var composedKey = composeItemKey(itemKey);

			expanded.Add(new UiRepeatItem(itemKey, () => Template(current, composedKey)));
		}

		return new UiRepeatExpansion(items, expanded);
	}
}

internal interface IUiRepeatElement
{
	UiRepeatExpansion Expand(Func<string, string> composeItemKey);
}

internal sealed class UiRepeatExpansion
{
	internal UiRepeatExpansion(object? itemList, List<UiRepeatItem> items)
	{
		ItemList = itemList;
		Items = items;
	}

	internal object? ItemList { get; }

	internal List<UiRepeatItem> Items { get; }
}

internal readonly record struct UiRepeatItem(string ItemKey, Func<UiElement> Build);
