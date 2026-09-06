namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// One contiguous run of a materialized node's child list, and the unit structural reconciliation works in.
///
/// <para>
/// A container's children are the concatenation of what each authored child materialized to. A plain element
/// contributes exactly one node; a <see cref="Dsl.UiWhen" /> or <see cref="Dsl.UiRepeat{TItem}" /> contributes
/// a nested region it owns, whose contents change when its condition flips or its item list changes. A
/// <see cref="Dsl.UiFragment" /> contributes its children straight into the enclosing region, because it is
/// fixed at materialization and never re-materializes on its own.
/// </para>
///
/// <para>
/// Keeping this shape - rather than a flat child list - is what makes an insert land at its <i>rendered</i>
/// index. A conditional's position among its authored siblings says nothing about where its node belongs once
/// the siblings that render nothing are gone, and an index computed from the authored position is either off by
/// the number of hidden siblings or out of bounds, which discards the whole patch.
/// </para>
/// </summary>
internal sealed class UiChildRegion
{
	private readonly List<UiChildEntry> _entries = [];
	private UiMaterializedNode? _owner;

	internal UiChildRegion(UiChildRegion? parent, UiStructuralScope? scope)
	{
		Parent = parent;
		Scope = scope;
	}

	/// <summary>The enclosing region, or <c>null</c> when this region's entries sit directly in
	/// <see cref="Owner" />'s child list.</summary>
	internal UiChildRegion? Parent { get; }

	/// <summary>The structural scope that owns this region, or <c>null</c> for a node's own root region.
	/// </summary>
	internal UiStructuralScope? Scope { get; }

	/// <summary>This region's position among <see cref="Parent" />'s entries. Fixed for as long as the parent
	/// region exists, since the authored structure a region tree mirrors does not change.</summary>
	internal int IndexInParent { get; private set; }

	/// <summary>
	/// The node whose child list this region contributes to. <c>null</c> for a slot that must hold exactly one
	/// node and therefore has no parent child list at all - the tree root and a
	/// <see cref="Dsl.UiElement.Fallback" />.
	/// </summary>
	internal UiMaterializedNode? Owner
	{
		get => _owner ?? Parent?.Owner;
		set => _owner = value;
	}

	/// <summary>How many nodes this region currently contributes.</summary>
	internal int Count
	{
		get
		{
			var count = 0;

			foreach (var entry in _entries)
			{
				count += entry.Region?.Count ?? 1;
			}

			return count;
		}
	}

	/// <summary>The index in <see cref="Owner" />'s child list at which this region's first node sits.
	/// </summary>
	internal int BaseIndex
	{
		get
		{
			var baseIndex = 0;

			for (var region = this; region.Parent is { } parent; region = parent)
			{
				for (var index = 0; index < region.IndexInParent; index++)
				{
					var entry = parent._entries[index];
					baseIndex += entry.Region?.Count ?? 1;
				}
			}

			return baseIndex;
		}
	}

	/// <summary>Appends this region's nodes to <paramref name="into" />, in render order.</summary>
	internal void Flatten(List<UiMaterializedNode> into)
	{
		foreach (var entry in _entries)
		{
			if (entry.Region is { } region)
			{
				region.Flatten(into);
			}
			else if (entry.Node is { } node)
			{
				into.Add(node);
			}
		}
	}

	/// <summary>Adds one materialized node at the end of this region.</summary>
	internal void AddNode(UiMaterializedNode node) => _entries.Add(new UiChildEntry(node, null));

	/// <summary>Opens a nested region for <paramref name="scope" /> at the end of this region and hands it to
	/// the scope, which reconciles into it for the rest of its life.</summary>
	internal UiChildRegion AddRegion(UiStructuralScope scope)
	{
		var region = new UiChildRegion(this, scope) { IndexInParent = _entries.Count };

		_entries.Add(new UiChildEntry(null, region));
		scope.Region = region;

		return region;
	}

	/// <summary>Drops every entry, which a scope does before it materializes its new contents into itself.
	/// </summary>
	internal void Clear() => _entries.Clear();

	/// <summary>Releases every structural scope nested inside this region, leaving this region's own
	/// <see cref="Scope" /> alone - a reconciling scope survives its own re-materialization, everything it
	/// contained does not.</summary>
	internal void ReleaseNestedScopes()
	{
		foreach (var entry in _entries)
		{
			if (entry.Region is not { } nested)
			{
				continue;
			}

			nested.Scope?.Release();
			nested.ReleaseNestedScopes();
		}
	}
}

/// <summary>One entry of a <see cref="UiChildRegion" />: either a materialized node or a nested region.
/// </summary>
internal readonly record struct UiChildEntry(UiMaterializedNode? Node, UiChildRegion? Region);
